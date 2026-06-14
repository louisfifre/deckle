"""Synthesize French TTS LOCALLY via Orpheus (ONNX-genai, DirectML) + SNAC (ONNX, CPU).

On-doctrine: no online inference, no `transformers` at inference. The 3B Orpheus
French finetune (canopylabs/3b-fr-ft-research_release) is exported once to
onnxruntime-genai by the model builder (export-time torch/transformers allowed);
here we drive it purely through `onnxruntime_genai` (LM on DirectML) and
`onnxruntime` (SNAC decoder on CPU — the AMD DML ConvTranspose wall keeps the
upsampling decoder off the GPU).

Pipeline (Orpheus -> SNAC):
  1. LM: prompt = [SOH] + tokenizer("<voice>: <text>") + [EOT, EOH, SOA, SOS];
     autoregress until end-of-speech (128258). The FR finetune exposes NAMED
     voices (pierre/amelie/marie) — a valid voice is REQUIRED; a voiceless or
     English-voice prompt yields corrupted ("alien") audio.
  2. token -> SNAC code: code = id - 128266 - (pos % 7) * 4096; 7 codes = 1 frame.
  3. de-interleave 7 codes -> 3 SNAC levels (c0=[0]; c1=[1,4]; c2=[2,3,5,6]).
  4. SNAC static decoder: 12 frames -> 24576 samples; window the stream by 12.

LM export: D:\\models\\tts\\orpheus-fr-genai-fp16-dml (fp16, DirectML provider).
SNAC: D:\\models\\tts\\snac-decoder\\snac24_int2wav_static.onnx (CPU).
Outputs land in the shared run dir; stats are recorded per voice.

Run with the dedicated venv:
    benchmark\\.venv-orpheus\\Scripts\\python.exe ^
        benchmark\\benches\\tts-audition\\orpheus_synth.py
"""

from __future__ import annotations

import sys
import time
import traceback
from pathlib import Path

import numpy as np
import soundfile as sf
import onnxruntime as ort
import onnxruntime_genai as og
from tokenizers import Tokenizer as HFTokenizer

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import _harness  # noqa: E402

OUT = _harness.RUN_DIR
LM_DIR = Path(r"D:\models\tts\orpheus-fr-genai-fp16-dml")
SNAC = Path(r"D:\models\tts\snac-decoder\snac24_int2wav_static.onnx")

# Orpheus special tokens (canonical, canopyai/Orpheus-TTS).
BOS = 128000           # <|begin_of_text|>
SOH = 128259           # start of human turn
EOT = 128009           # <|eot_id|> end of text
EOH = 128260           # end of human turn
SOA = 128261           # start of AI turn (model generates this)
SOS = 128257           # start of speech (model generates this)
EOS_AUDIO = 128258     # end of speech (generation stop)
CODE_OFFSET = 128266   # first SNAC speech-token id (= 128256 + 10)

# SNAC int2wav_static (laion/SNAC-24khz-decoder-onnx) decodes a FIXED window of 12
# coarse frames -> 24576 samples (2048 samples/frame). It must be driven as a
# sliding CENTRE window, not disjoint blocks: keep the middle, discard a margin of
# convolutional context at each interior edge, edge-pad at the extremities.
WIN_FRAMES = 12
WIN_SAMPLES = 24576
SAMPLES_PER_FRAME = WIN_SAMPLES // WIN_FRAMES  # 2048
KEEP_MARGIN = 1  # frames of context dropped at each interior window edge
SR = 24000

# The FR finetune's named voices (lowercase). A valid voice is required.
VOICES = ["pierre", "amelie", "marie"]
# Clean, normal full sentences only — no hesitation tics (04), no expressive tags
# (read literally on this finetune), no short corpus fragments. Real prose so the
# voice can actually be judged.
SIDS = ["01_neutre", "02_explication", "03_emotion", "05_question"]

# Sampling — lower temperature than the 0.6 default for more coherent French
# (the garbling/hesitation suggests the decode samples drift; tighten it).
TEMPERATURE = 0.4
TOP_P = 0.9
TOP_K = 50
REPETITION_PENALTY = 1.1
MAX_NEW = 1800


def log(m: str) -> None:
    print(m, flush=True)


def build_prompt_ids(tokenizer: HFTokenizer, text: str, voice: str) -> list[int]:
    """Frame "<voice>: <text>" with Orpheus special tokens -> flat id list.

    Tokenized via the `tokenizers` lib (reading tokenizer.json), NOT og.Tokenizer:
    transformers 5.x writes a tokenizer_class that genai 0.13.1 rejects
    (TokenizersBackend). add_special_tokens=False returns only the text tokens —
    the SOH/EOT/EOH/SOA/SOS framing is added explicitly here.
    """
    text_ids = list(tokenizer.encode(f"{voice}: {text}", add_special_tokens=False).ids)
    # canopyai canonical: [SOH] + [BOS]+text + [EOT, EOH]. The model GENERATES the
    # [SOA][SOS] speech-start itself — pre-filling them yields short/empty turns.
    return [SOH, BOS] + text_ids + [EOT, EOH]


def generate_speech_tokens(model, tokenizer, prompt_ids, max_new=MAX_NEW):
    """Autoregress until EOS_AUDIO (or cap); return the generated token ids."""
    params = og.GeneratorParams(model)
    params.set_search_options(
        do_sample=True, temperature=TEMPERATURE, top_p=TOP_P, top_k=TOP_K,
        repetition_penalty=REPETITION_PENALTY, max_length=len(prompt_ids) + max_new,
    )
    gen = og.Generator(model, params)
    gen.append_tokens(prompt_ids)
    produced: list[int] = []
    while not gen.is_done():
        gen.generate_next_token()
        tok = int(gen.get_next_tokens()[0])
        if tok == EOS_AUDIO:
            break
        produced.append(tok)
        if len(produced) >= max_new:
            break
    return produced


def tokens_to_codes(tokens: list[int]) -> list[int]:
    """Keep only SNAC speech tokens and map them to raw codes via running position.

    Orpheus: code = id - CODE_OFFSET - (pos % 7) * 4096. `pos` is the running
    index over the kept speech tokens; a token is a speech token when its code
    lands in [0, 4096). Anything else (stray text/special) is skipped, and the
    position counter only advances on accepted codes so frame alignment holds.
    Out-of-range codes are the canonical signature of a bad offset / wrong voice.
    """
    codes: list[int] = []
    pos = 0
    for tok in tokens:
        if tok < CODE_OFFSET:
            continue
        code = tok - CODE_OFFSET - (pos % 7) * 4096
        if 0 <= code < 4096:
            codes.append(code)
            pos += 1
    return codes


def deinterleave(frame7: list[int]) -> tuple[list[int], list[int], list[int]]:
    """One 7-code frame -> (c0[1], c1[2], c2[4]) per the canonical Orpheus map."""
    c0 = [frame7[0]]
    c1 = [frame7[1], frame7[4]]
    c2 = [frame7[2], frame7[3], frame7[5], frame7[6]]
    return c0, c1, c2


def _decode_window(session: ort.InferenceSession, frames: list, start: int,
                   n_frames: int) -> np.ndarray:
    """Decode one 12-frame window, edge-padding (clamping) out-of-range frames."""
    c0, c1, c2 = [], [], []
    for k in range(WIN_FRAMES):
        idx = min(max(start + k, 0), n_frames - 1)  # clamp = repeat edge frame
        a, b, c = deinterleave(frames[idx])
        c0 += a
        c1 += b
        c2 += c
    feeds = {
        "codes0": np.asarray(c0, dtype=np.int64).reshape(1, WIN_FRAMES),
        "codes1": np.asarray(c1, dtype=np.int64).reshape(1, 2 * WIN_FRAMES),
        "codes2": np.asarray(c2, dtype=np.int64).reshape(1, 4 * WIN_FRAMES),
    }
    return session.run(None, feeds)[0].reshape(-1).astype(np.float32)


def decode_codes(session: ort.InferenceSession, codes: list[int]) -> np.ndarray:
    """Sliding-centre-window SNAC decode (laion static-decoder reference scheme).

    Window = 12 frames, hop = 10, keep the centre [2048:22528] with a 1-frame
    margin of convolutional context discarded at each interior edge; the first
    window keeps its real start, the last its real end. Decoding disjoint blocks
    (the previous approach) lost edge context and garbled everything past ~1 s.
    """
    n_frames = len(codes) // 7
    if n_frames == 0:
        return np.zeros(0, dtype=np.float32)
    frames = [codes[7 * j: 7 * j + 7] for j in range(n_frames)]
    spf = SAMPLES_PER_FRAME

    if n_frames <= WIN_FRAMES:  # fits a single window
        return _decode_window(session, frames, 0, n_frames)[: n_frames * spf]

    hop = WIN_FRAMES - 2 * KEEP_MARGIN  # 10
    out: list[np.ndarray] = []
    s = 0
    first = True
    while True:
        wav = _decode_window(session, frames, s, n_frames)
        is_last = (s + WIN_FRAMES >= n_frames)
        left = 0 if first else KEEP_MARGIN
        right = 0 if is_last else KEEP_MARGIN
        gend = min(s + WIN_FRAMES - right, n_frames)
        out.append(wav[left * spf: (gend - s) * spf])
        if is_last:
            break
        s += hop
        first = False
    return np.concatenate(out)[: n_frames * spf]


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    log(f"Output: {OUT}\nLM: {LM_DIR}\nSNAC: {SNAC}")

    if not (LM_DIR / "model.onnx").exists():
        log("  FATAL: LM export missing (model.onnx). Run the genai builder (-p fp16 -e dml) first.")
        return 2
    if not SNAC.exists():
        log("  FATAL: SNAC decoder onnx missing.")
        return 2

    texts = {**_harness.PUBLIC_SENTENCES, **_harness.EXPRESSIVE_TAGS, **_harness.corpus_sentences()}
    sids = [s for s in SIDS if s in texts]

    log("\nLoading SNAC decoder (CPU EP) ...")
    snac = ort.InferenceSession(str(SNAC), providers=["CPUExecutionProvider"])
    for i in snac.get_inputs():
        log(f"  SNAC in  {i.name} {i.shape} {i.type}")

    log("Loading Orpheus LM (onnxruntime_genai) ...")
    t0 = time.time()
    model = og.Model(og.Config(str(LM_DIR)))
    tokenizer = HFTokenizer.from_file(str(LM_DIR / "tokenizer.json"))
    load_s = time.time() - t0
    log(f"  LM ready in {load_s:.1f}s  (execution provider from genai_config.json)")

    for voice in VOICES:
        log(f"\n===== voice {voice} =====")
        comp = aud = 0.0
        n = 0
        for sid in sids:
            text = texts[sid]
            dest = OUT / f"onnx_orpheus_{voice}_{sid}.wav"
            try:
                tc = time.time()
                prompt_ids = build_prompt_ids(tokenizer, text, voice)
                toks = generate_speech_tokens(model, tokenizer, prompt_ids)
                codes = tokens_to_codes(toks)
                n_frames = len(codes) // 7
                if n_frames == 0:
                    log(f"  {sid}: EMPTY (no SNAC speech tokens) — prompt/voice off?")
                    continue
                audio = decode_codes(snac, codes)
                comp += time.time() - tc
                peak = float(np.max(np.abs(audio))) if audio.size else 0.0
                if peak > 0:
                    audio = audio / max(peak, 1e-6) * 0.95  # gentle normalize
                sf.write(str(dest), audio.astype(np.float32), SR)
                secs = audio.size / SR
                aud += secs
                n += 1
                log(f"  {sid}: {len(toks)} tok -> {n_frames} frames, {secs:.1f}s (peak {peak:.2f})")
            except Exception as e:  # noqa: BLE001
                log(f"  GEN FAILED {voice}/{sid}: {type(e).__name__}: {str(e)[:200]}")
                traceback.print_exc()
        if n:
            _harness.stats_record(
                "orpheus", voice, ep="dml", n=n,
                compute_s=round(comp, 1), audio_s=round(aud, 1),
                rtf=round(aud / comp, 2) if comp else None, load_s=round(load_s, 1))

    wavs = sorted(OUT.glob("onnx_orpheus_*.wav"))
    log(f"\nDone. {len(wavs)} orpheus wavs:")
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
