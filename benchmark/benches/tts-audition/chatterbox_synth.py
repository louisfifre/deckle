"""Synthesize French Chatterbox-Multilingual samples LOCALLY via pure ONNX Runtime.

Chatterbox-Multilingual (Resemble AI, ONNX export by onnx-community) is the
expressive multilingual candidate: 23 languages incl. first-class French, a
LEARNED emotion dial (the `exaggeration` scalar fed to embed_tokens), and a
zero-shot speaker-cloning front-end (speech_encoder reads a reference wav). It
runs as FOUR onnx graphs on plain onnxruntime — no torch, no `transformers` at
inference. Tokenization is the Rust `tokenizers` lib reading tokenizer.json; its
TemplateProcessing post-processor reproduces exactly the id stream the original
HF AutoTokenizer emits (<EXAGGERATION> <s> ...text... </s> <START_SPEECH>
<START_SPEECH>), so we stay off `transformers` with no behavioural drift.

The autoregressive LM is the fp16 export (NOT q4 — project no-Q4 ASR/TTS
doctrine; fp16 is the high-precision variant, not a 4-bit quantization).
Execution provider is per-session via DECKLE_TTS_EP (cpu|dml): the three
transformer/encoder graphs ride DirectML when asked, but the conditional_decoder
is ConvTranspose-heavy and is ALWAYS pinned to CPU (the AMD DML 80070057 wall has
no auto-fallback). CPU vs GPU only moves latency, never the voice.

Decode follows the resemble-ai PRODUCTION recipe (sampled, ~1000-token budget),
not the onnx-community DEMO recipe (greedy argmax, 256 cap) which truncated long
sentences and clipped short ones.

Weights live under D:\\models\\tts\\chatterbox (outside the repo), downloaded once
from onnx-community/chatterbox-multilingual-ONNX. Outputs land in the gitignored
run dir. The Perth watermark is deliberately dropped (audition, not distribution).

Run with the dedicated venv:
    benchmark\\.venv-chatterbox\\Scripts\\python.exe benchmark\\benches\\tts-audition\\chatterbox_synth.py
"""

from __future__ import annotations

import os
import sys
import time
import traceback
from pathlib import Path

import numpy as np
import librosa
import soundfile as sf
import onnxruntime as ort
from tokenizers import Tokenizer

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))  # make the shared _harness importable when run as a file
import _harness  # noqa: E402

OUT = _harness.RUN_DIR
MODEL_DIR = Path(r"D:\models\tts\chatterbox")
ONNX_DIR = MODEL_DIR / "onnx"

# Verified against the model card recipe + onnx graph introspection.
S3GEN_SR = 24000
START_SPEECH_TOKEN = 6561
STOP_SPEECH_TOKEN = 6562
NUM_HIDDEN_LAYERS = 30
NUM_KV_HEADS = 16
HEAD_DIM = 64
LANG = "fr"

# Decode recipe — resemble-ai PRODUCTION defaults (sampled), not the onnx-community
# DEMO (greedy argmax, 256 cap). The demo caused the dual failure we heard: long
# sentences hit the 256 ceiling (trailing silence) while short ones argmax'd onto
# STOP too early (clipped). Sampling + a ~1000-token budget fixes both.
REPETITION_PENALTY = 1.2
MAX_NEW_TOKENS = 1000
TEMPERATURE = 0.8
TOP_P = 0.95
TOP_K = 1000
SEED = 9527  # reproducible sampled decode

DEFAULT_EXAGGERATION = 0.5

# ── Accent experiment ──────────────────────────────────────────────────────
# Chatterbox CLONES the reference clip's timbre AND its accent. The shipped
# default_voice.wav is an ENGLISH speaker -> anglo-accented French (what Louis
# heard). We probe whether a NATIVE FRENCH reference removes the accent, using
# clean French clips already produced in this run dir (Supertonic M1, Piper
# Pierre/Jessica) as zero-shot references. Plus one "flat voice" pass — lower
# temperature + exaggeration — for the "voix plus plate" request. The default
# (anglo) takes stay on the page as the baseline; we don't re-render them.
FR_REFERENCES = {
    "frSupertonic": OUT / "onnx_supertonic_M1_01_neutre.wav",
    "frPierre": OUT / "onnx_piper_upmc_s1_01_neutre.wav",
    "frJessica": OUT / "onnx_piper_upmc_s0_01_neutre.wav",
}
COMPARISON_SIDS = ["01_neutre", "02_explication", "corpus_01"]
FLAT_REF = "frPierre"
FLAT_EXAGGERATION = 0.3
FLAT_TEMPERATURE = 0.5


def log(m: str) -> None:
    print(m, flush=True)


def repetition_penalty(generated: np.ndarray, scores: np.ndarray, penalty: float) -> np.ndarray:
    """HF-equivalent repetition penalty: down-weight already-emitted tokens.

    Positive logits are divided by `penalty`, negative ones multiplied — both
    push the score of seen tokens toward zero, discouraging loops.
    """
    score = np.take_along_axis(scores, generated, axis=1)
    score = np.where(score < 0, score * penalty, score / penalty)
    out = scores.copy()
    np.put_along_axis(out, generated, score, axis=1)
    return out


def sample_token(logits: np.ndarray, rng: np.random.Generator, *, temperature: float) -> int:
    """Temperature + top-k + top-p (nucleus) sampling over one step's logits.

    `logits` is the 1-D, already-repetition-penalized score vector. This replaces
    greedy argmax — the production decode that stops the long-sentence runaway
    and the short-sentence premature STOP. `temperature` is per-call so a "flat
    voice" pass can lower it without touching the module default.
    """
    z = logits.astype(np.float64) / temperature
    if TOP_K and TOP_K < z.size:
        kth = np.partition(z, -TOP_K)[-TOP_K]
        z[z < kth] = -np.inf
    z -= np.max(z)
    probs = np.exp(z)
    probs /= probs.sum()
    if TOP_P < 1.0:
        order = np.argsort(probs)[::-1]
        cutoff = int(np.searchsorted(np.cumsum(probs[order]), TOP_P)) + 1
        keep = order[:cutoff]
        mask = np.zeros_like(probs, dtype=bool)
        mask[keep] = True
        probs = np.where(mask, probs, 0.0)
        probs /= probs.sum()
    return int(rng.choice(probs.size, p=probs))


def build_sessions() -> dict[str, ort.InferenceSession]:
    """Open the four graphs with per-session EP (DECKLE_TTS_EP). The three
    transformer/encoder graphs can ride DirectML; conditional_decoder is
    ConvTranspose-bearing and is force-pinned to CPU (AMD DML wall)."""
    so = ort.SessionOptions()
    so.intra_op_num_threads = 0  # let ORT pick; CPU-bound, real-time-ish
    non_ct = _harness.providers(convtranspose=False)
    ct = _harness.providers(convtranspose=True)
    sess = {
        "speech_encoder": ort.InferenceSession(str(ONNX_DIR / "speech_encoder.onnx"), so, providers=non_ct),
        "embed_tokens": ort.InferenceSession(str(ONNX_DIR / "embed_tokens.onnx"), so, providers=non_ct),
        "language_model": ort.InferenceSession(str(ONNX_DIR / "language_model_fp16.onnx"), so, providers=non_ct),
        "conditional_decoder": ort.InferenceSession(str(ONNX_DIR / "conditional_decoder.onnx"), so, providers=ct),
    }
    for name, s in sess.items():
        log(f"  EP {name}: {s.get_providers()[0]}")
    return sess


def encode_reference(sess: dict, ref_wav: Path):
    """Run the speaker front-end once; outputs are reused across all sentences.

    speech_encoder outputs (by name, confirmed via get_outputs):
      audio_features [1,T,1024]  -> cond_emb (prepended to text embeds)
      audio_tokens   [1,Ta]      -> prompt_token (prefix of the decoder tokens)
      speaker_embeddings [1,192] -> decoder `speaker_embeddings`
      speaker_features [1,F,80]  -> decoder `speaker_features`
    """
    audio, _ = librosa.load(str(ref_wav), sr=S3GEN_SR)
    audio = audio[np.newaxis, :].astype(np.float32)
    cond_emb, prompt_token, speaker_embeddings, speaker_features = sess["speech_encoder"].run(
        None, {"audio_values": audio})
    return cond_emb, prompt_token, speaker_embeddings, speaker_features


def synth(sess: dict, ref, tok: Tokenizer, text: str, exaggeration: float,
          temperature: float, rng: np.random.Generator) -> np.ndarray:
    """Full text->waveform for one sentence. `ref` is the encode_reference tuple."""
    cond_emb, prompt_token, speaker_embeddings, speaker_features = ref

    # Tokenize. The Rust post-processor adds <EXAGGERATION> <s> ... </s> and a
    # trailing pair of <START_SPEECH>; the language tag is a single added token.
    input_ids = np.array([tok.encode(f"[{LANG}]{text}").ids], dtype=np.int64)

    # Text tokens get incremental positions (arange-1); the trailing speech
    # markers (id >= START_SPEECH) are pinned to position 0 — the recipe's design.
    position_ids = np.where(
        input_ids >= START_SPEECH_TOKEN,
        0,
        np.arange(input_ids.shape[1])[np.newaxis, :] - 1,
    ).astype(np.int64)

    embed_inputs = {
        "input_ids": input_ids,
        "position_ids": position_ids,
        "exaggeration": np.array([exaggeration], dtype=np.float32),
    }

    generated = np.array([[START_SPEECH_TOKEN]])  # generation seed token
    attention_mask = None
    batch_size = 1
    past = {
        f"past_key_values.{l}.{kv}": np.zeros([batch_size, NUM_KV_HEADS, 0, HEAD_DIM], dtype=np.float16)
        for l in range(NUM_HIDDEN_LAYERS)
        for kv in ("key", "value")
    }

    for step in range(MAX_NEW_TOKENS):
        inputs_embeds = sess["embed_tokens"].run(None, embed_inputs)[0]
        if step == 0:
            # Prepend the audio-conditioning embedding to the text embeddings.
            inputs_embeds = np.concatenate((cond_emb, inputs_embeds), axis=1)
            seq_len = inputs_embeds.shape[1]
            attention_mask = np.ones((batch_size, seq_len), dtype=np.int64)

        logits, *present = sess["language_model"].run(None, dict(
            inputs_embeds=inputs_embeds.astype(np.float32),
            attention_mask=attention_mask,
            **past,
        ))

        logits = logits[:, -1, :]
        logits = repetition_penalty(generated, logits, REPETITION_PENALTY)
        nxt = sample_token(logits[0], rng, temperature=temperature)
        next_token = np.array([[nxt]], dtype=np.int64)
        generated = np.concatenate((generated, next_token), axis=-1)
        if (next_token.flatten() == STOP_SPEECH_TOKEN).all():
            break

        # Single-token step: new position is step+1, mask grows by one, KV rolls.
        embed_inputs["input_ids"] = next_token
        embed_inputs["position_ids"] = np.full((batch_size, 1), step + 1, dtype=np.int64)
        attention_mask = np.concatenate(
            [attention_mask, np.ones((batch_size, 1), dtype=np.int64)], axis=1)
        for j, key in enumerate(past):
            past[key] = present[j]

    # Drop the seed and the trailing stop; prefix the reference prompt tokens.
    speech_tokens = generated[:, 1:-1]
    speech_tokens = np.concatenate([prompt_token, speech_tokens], axis=1)

    wav = sess["conditional_decoder"].run(None, {
        "speech_tokens": speech_tokens.astype(np.int64),
        "speaker_embeddings": speaker_embeddings,
        "speaker_features": speaker_features,
    })[0]
    return np.squeeze(wav, axis=0)


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    log(f"Output: {OUT}\nModel dir: {MODEL_DIR}")
    tokenizer = Tokenizer.from_file(str(MODEL_DIR / "tokenizer.json"))

    # Texts read at runtime (public + private corpus). We render a small fixed
    # comparison set across several reference voices to isolate the ACCENT.
    texts = {**_harness.PUBLIC_SENTENCES, **_harness.corpus_sentences()}
    comparison = [(sid, texts[sid]) for sid in COMPARISON_SIDS if sid in texts]
    missing = [sid for sid in COMPARISON_SIDS if sid not in texts]
    if missing:
        log(f"  (comparison sids not found, skipped: {missing})")

    log("Building ONNX sessions…")
    t0 = time.perf_counter()
    sess = build_sessions()
    rng = np.random.default_rng(SEED)
    setup_s = time.perf_counter() - t0
    log(f"  sessions ready in {setup_s:.1f}s")

    # (refkey, ref_wav, exaggeration, temperature): native-FR refs at the default
    # dial, then one flat pass (low temp + low exaggeration) on the Pierre ref.
    runs = [(k, p, DEFAULT_EXAGGERATION, TEMPERATURE) for k, p in FR_REFERENCES.items()]
    runs.append(("flatPierre", FR_REFERENCES[FLAT_REF], FLAT_EXAGGERATION, FLAT_TEMPERATURE))

    tot_comp = tot_aud = 0.0
    tot_n = 0
    for refkey, ref_wav, exg, temp in runs:
        if not ref_wav.exists():
            log(f"\n== {refkey}: reference missing ({ref_wav.name}) — skipped ==")
            continue
        log(f"\n== {refkey}  (ref={ref_wav.name}, exg={exg}, temp={temp}) ==")
        ref = encode_reference(sess, ref_wav)
        for sid, text in comparison:
            dest = OUT / f"onnx_chatterbox_{refkey}_{sid}.wav"
            try:
                tc = time.perf_counter()
                wav = synth(sess, ref, tokenizer, text, exg, temp, rng)
                tot_comp += time.perf_counter() - tc
                sf.write(str(dest), wav.astype(np.float32), S3GEN_SR)
                secs = len(wav) / S3GEN_SR
                tot_aud += secs
                tot_n += 1
                log(f"  wrote {dest.name}  ({secs:.1f}s)")
            except Exception as e:  # noqa: BLE001
                log(f"  GEN FAILED {refkey}/{sid}: {type(e).__name__}: {str(e)[:200]}")
                traceback.print_exc()
    if tot_n:
        _harness.stats_record(
            "chatterbox", "refsweep", ep=os.environ.get("DECKLE_TTS_EP", "cpu"), n=tot_n,
            compute_s=round(tot_comp, 1), audio_s=round(tot_aud, 1),
            rtf=round(tot_aud / tot_comp, 2) if tot_comp else None, load_s=round(setup_s, 1))

    wavs = sorted(OUT.glob("onnx_chatterbox_*.wav"))
    log(f"\nDone. {len(wavs)} Chatterbox wavs total in run dir:")
    for w in wavs:
        log(f"  {w.name}  ({w.stat().st_size // 1024} KB)")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        raise SystemExit(1)
