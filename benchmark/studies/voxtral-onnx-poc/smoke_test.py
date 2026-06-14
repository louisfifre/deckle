"""Smoke test Voxtral Mini 3B 2507 ONNX sur la voie 2 (DirectML).

Objectif validation breadth : confirmer que le pipeline ONNX produit des
transcriptions cohérentes sur (a) plusieurs variantes de quantization
decoder (FP16, Q4F16) et (b) plusieurs durées audio (court / long-form).

Pipeline porté du code de référence de la discussion #1 du repo
`onnx-community/Voxtral-Mini-3B-2507-ONNX` (auteur urroxyz, validé après
les fixes Xenova des 21-22 juillet 2025). Adapté avec le format de
prompt **transcription canonique** Voxtral officiel :

    <s>[INST][BEGIN_AUDIO][AUDIO]×N[/INST] lang:fr [TRANSCRIBE]

Distinct du format chat (apply_chat_template) qui omet [BEGIN_AUDIO] et
met l'instruction texte INSIDE le INST block. Sur audio court silencieux
le chat-mode peut paraphraser ; le canonique cible la transcription pure.

Approche actuelle : génération **sans KV cache** (recompute le prompt
complet à chaque step). O(N²) en compute mais établit une référence
fonctionnelle propre. L'optimisation KV-cache est un follow-up tracké
séparément — la version naïve avec mapping `present.* → past_key_values.*`
ne fonctionne pas malgré le buffer reuse contourné par `.copy()`, cause
non identifiée à ce stade.

Tous les `.onnx` et `.onnx_data*` vivent sous D:\\models\\llm\\ pour
ne pas dupliquer entre worktrees (cf. CLAUDE.md benchmark MODELS_DIR).
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
from pathlib import Path

import numpy as np
import librosa
import soundfile as sf
import onnxruntime as ort
from huggingface_hub import snapshot_download
from tokenizers import Tokenizer


if sys.stdout.encoding and sys.stdout.encoding.lower() not in {"utf-8", "utf8"}:
    sys.stdout.reconfigure(encoding="utf-8")


REPO_ID    = "onnx-community/Voxtral-Mini-3B-2507-ONNX"
MODELS_DIR = Path(r"D:\models\llm\voxtral-mini-3b-2507-onnx")

# IDs token Mistral Tekken validés par debug_tokens.py.
BOS_ID, INST_ID, BAUD_ID, AUD_ID, EINST_ID, EOS_ID, TRANSCRIBE_ID = 1, 3, 25, 24, 4, 2, 34

TARGET_SR        = 16000
EXPECTED_FRAMES  = 3000           # 30s × 100 fps (hop 10ms)
EXPECTED_SAMPLES = EXPECTED_FRAMES * 160


def step(msg: str) -> None:
    print(f"\n── {msg}", flush=True)


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Smoke test Voxtral ONNX")
    p.add_argument("--decoder", choices=["fp16", "q4f16", "q4"], default="fp16",
                   help="Variante du decoder (fp16 par défaut, q4f16 pour test quantization).")
    p.add_argument("--audio", type=Path, default=None,
                   help="Path WAV à transcrire. Par défaut : dcad692a (1.4s, 'Et toujours douter un peu.').")
    p.add_argument("--lang", default="fr", help="Code langue pour le prompt 'lang:XX [TRANSCRIBE]'.")
    p.add_argument("--max-new", type=int, default=128, help="Tokens max à générer.")
    return p.parse_args()


def default_audio_path() -> Path:
    return Path(
        os.environ.get("LOCALAPPDATA", "")
    ) / "Deckle" / "benchmark" / "corpora" / "voxtral-val-30" / "dcad692a54fd452cbfb174ca9899deba.wav"


def compute_mel(audio_path: Path) -> np.ndarray:
    """Whisper-style 128-bin log-mel spectrogram, padded/truncated à 30s."""
    y, sr = sf.read(str(audio_path))
    if y.ndim > 1:
        y = y.mean(axis=1)
    if sr != TARGET_SR:
        y = librosa.resample(y, orig_sr=sr, target_sr=TARGET_SR)
    dur_s = len(y) / TARGET_SR
    if len(y) < EXPECTED_SAMPLES:
        y = np.pad(y, (0, EXPECTED_SAMPLES - len(y)))
    y = y[:EXPECTED_SAMPLES].astype(np.float32)
    mel = librosa.feature.melspectrogram(
        y=y, sr=TARGET_SR, n_fft=400, hop_length=160, n_mels=128,
    )
    log_spec = np.log10(np.maximum(mel, 1e-10))
    log_spec = np.maximum(log_spec, log_spec.max() - 8.0)
    log_spec = (log_spec + 4.0) / 4.0
    log_spec = log_spec.astype(np.float32)[:, :EXPECTED_FRAMES]
    return log_spec[None, :], dur_s


def main() -> int:
    args = parse_args()
    audio_path = args.audio or default_audio_path()
    decoder_filename = f"decoder_model_merged_{args.decoder}.onnx"

    t_total = time.perf_counter()

    # ─── 1. Snapshot download (FP16 encoder + embed + chosen decoder) ────
    step(f"1. snapshot_download → {MODELS_DIR}")
    t0 = time.perf_counter()
    allow = [
        "onnx/audio_encoder_fp16.onnx*",
        "onnx/embed_tokens_fp16.onnx*",
        f"onnx/{decoder_filename}*",
        "tokenizer.json", "config.json", "generation_config.json",
        "preprocessor_config.json", "chat_template.jinja",
        "special_tokens_map.json", "tekken.json",
    ]
    local_dir = snapshot_download(
        repo_id=REPO_ID, repo_type="model",
        local_dir=str(MODELS_DIR), allow_patterns=allow,
    )
    print(f"   local_dir = {local_dir}")
    print(f"   decoder   = {decoder_filename}")
    print(f"   elapsed   = {time.perf_counter() - t0:.1f}s")

    # ─── 2. Load tokenizer + 3 sessions DirectML ─────────────────────────
    step(f"2. Load tokenizer + sessions (decoder={args.decoder}, EP=DirectML)")
    t0 = time.perf_counter()
    tok = Tokenizer.from_file(str(Path(local_dir) / "tokenizer.json"))
    providers = ["DmlExecutionProvider", "CPUExecutionProvider"]
    onnx_dir  = Path(local_dir) / "onnx"

    ae   = ort.InferenceSession(str(onnx_dir / "audio_encoder_fp16.onnx"), providers=providers)
    emb  = ort.InferenceSession(str(onnx_dir / "embed_tokens_fp16.onnx"),  providers=providers)
    dec  = ort.InferenceSession(str(onnx_dir / decoder_filename),          providers=providers)
    cfg  = json.loads((Path(local_dir) / "config.json").read_text(encoding="utf-8"))
    txt  = cfg.get("text_config", cfg)
    nh, nkv = txt["num_attention_heads"], txt["num_key_value_heads"]
    hd      = txt.get("head_dim", txt["hidden_size"] // nh)
    print(f"   txt_cfg: n_heads={nh}, n_kv={nkv}, head_dim={hd}")
    print(f"   load elapsed = {time.perf_counter() - t0:.1f}s")

    # ─── 3. Audio → mel ───────────────────────────────────────────────────
    step(f"3. Audio + mel ({audio_path.name})")
    if not audio_path.exists():
        print(f"   FAIL audio introuvable : {audio_path}")
        return 2
    mel_features, dur_s = compute_mel(audio_path)
    print(f"   dur = {dur_s:.2f}s | mel shape = {mel_features.shape}")

    # ─── 4. Audio encoder ─────────────────────────────────────────────────
    step("4. Audio encoder (FP16)")
    t0 = time.perf_counter()
    audio_embeds = ae.run(None, {ae.get_inputs()[0].name: mel_features})[0]
    if audio_embeds.ndim == 3:
        audio_embeds = audio_embeds[0]
    n_audio = audio_embeds.shape[0]
    if np.isnan(audio_embeds).any():
        print(f"   FAIL audio_embeds NaN (bug Xenova de retour ?)")
        return 3
    print(f"   audio_embeds: shape={audio_embeds.shape} | {n_audio} tokens")
    print(f"   stats: mean={audio_embeds.mean():+.3f} std={audio_embeds.std():.3f}")
    print(f"   encoder elapsed = {time.perf_counter() - t0:.2f}s")

    # ─── 5. Build prompt transcription canonique ─────────────────────────
    step(f"5. Build prompt (lang:{args.lang} [TRANSCRIBE])")
    suffix_ids = tok.encode(f" lang:{args.lang} [TRANSCRIBE]", add_special_tokens=False).ids
    prompt_ids = (
        [BOS_ID, INST_ID, BAUD_ID]
        + [AUD_ID] * n_audio
        + [EINST_ID]
        + suffix_ids
    )
    print(f"   prompt: BOS INST BEGIN_AUDIO AUDIO×{n_audio} /INST 'lang:{args.lang} [TRANSCRIBE]'")
    print(f"   prompt_ids length = {len(prompt_ids)}")

    input_ids = np.array([prompt_ids], dtype=np.int64)
    emb_input  = emb.get_inputs()[0].name
    emb_output = emb.get_outputs()[0].name
    inputs_embeds = emb.run([emb_output], {emb_input: input_ids})[0].astype(np.float32)
    inputs_embeds[0, 3 : 3 + n_audio, :] = audio_embeds.astype(np.float32)

    # ─── 6. Decoder loop (sans KV cache, full re-run) ────────────────────
    step(f"6. Decoder loop greedy (max_new={args.max_new}, no-KV — O(N²))")
    t0 = time.perf_counter()
    dec_input_names = [i.name for i in dec.get_inputs()]
    kv_input_names  = [n for n in dec_input_names if "past_key_values" in n]

    def empty_past() -> dict:
        return {n: np.zeros((1, nkv, 0, hd), dtype=np.float16) for n in kv_input_names}

    current_embeds = inputs_embeds.copy()
    generated: list[int] = []
    for step_i in range(args.max_new):
        cur_len = current_embeds.shape[1]
        feed = {
            "inputs_embeds":  current_embeds,
            "attention_mask": np.ones((1, cur_len), dtype=np.int64),
            "position_ids":   np.arange(cur_len, dtype=np.int64)[None, :],
            **empty_past(),
        }
        outputs = dec.run(None, feed)
        logits  = outputs[0]
        next_id = int(np.argmax(logits[0, -1, :]))
        generated.append(next_id)

        if step_i < 3:
            top5 = np.argsort(-logits[0, -1, :])[:5].tolist()
            top5_str = [tok.decode([t], skip_special_tokens=False) for t in top5]
            print(f"   step {step_i:3d}: next={next_id} {tok.decode([next_id], skip_special_tokens=False)!r}, top5={list(zip(top5, top5_str))}")

        if next_id == EOS_ID:
            print(f"   EOS at step {step_i}")
            break

        next_embed = emb.run(None, {emb_input: np.array([[next_id]], dtype=np.int64)})[0].astype(np.float32)
        current_embeds = np.concatenate([current_embeds, next_embed], axis=1)

    dec_elapsed = time.perf_counter() - t0
    print(f"   generated {len(generated)} tokens in {dec_elapsed:.1f}s ({dec_elapsed/max(len(generated),1):.2f}s/tok)")

    # ─── 7. Decode + render ───────────────────────────────────────────────
    step("7. Result")
    text = tok.decode(generated, skip_special_tokens=True).strip()
    print(f"   decoder    : {args.decoder}")
    print(f"   audio      : {audio_path.name} ({dur_s:.2f}s)")
    print(f"   hypothesis : {text!r}")
    print(f"\n── TOTAL elapsed: {time.perf_counter() - t_total:.1f}s")
    return 0


if __name__ == "__main__":
    sys.exit(main())
