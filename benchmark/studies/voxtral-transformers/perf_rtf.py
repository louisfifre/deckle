"""Mesure perf RTF de Voxtral Mini 3B BF16 sur Transformers + torch ROCm.

Charge le modèle une fois, fait un warm-up jeté, puis transcrit trois
samples de durée croissante (court 1.75s, moyen 29.17s, long 113.95s) en
mesurant temps preprocess + generate + decode et RTF (temps inférence
total / durée audio). Cible : caractériser le RTF en steady-state pour
décider si Voxtral via Transformers est viable comme runtime de
production pour la dictée Deckle, ou seulement comme vérité de terrain.

Le RTF de référence à dépasser sur la même machine :
  - Whisper.cpp large-v3   : ~0.05-0.10
  - llama-mtmd-cli (GGUF)  : ~0.05-0.50 selon quant
Un RTF >1 signifie « plus lent que le temps réel », rédhibitoire pour la
dictée interactive.

Usage :
    .venv-voxtral-rocm\\Scripts\\python.exe benches\\voxtral-transformers\\perf_rtf.py
"""

from __future__ import annotations

import io
import json
import os
import sys
import time
from pathlib import Path

if sys.stdout.encoding.lower() != "utf-8":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8")

import torch
from transformers import AutoProcessor, VoxtralForConditionalGeneration


MODEL_PATH = Path(r"D:\models\llm\voxtral\Voxtral-Mini-3B-2507-safetensors")
HF_REPO_ID = "mistralai/Voxtral-Mini-3B-2507"
CORPUS_DIR = Path(os.environ["LOCALAPPDATA"]) / "Deckle" / "benchmark" / "corpora" / "voxtral-val-30"

WARMUP_SAMPLE_ID = "b9d726f4577a4fda998110044bb1109c"  # 1.06s, jeté

SAMPLES = [
    ("court ",  1.75,   "dcad692a54fd452cbfb174ca9899deba"),
    ("moyen ", 29.17,   "701ce47a167f40f1b49c3a32a446358b"),
    ("long  ", 113.95,  "677a1dee82164efbb3536ef1581b8c9e"),
]


def fmt_bytes(n: float) -> str:
    for unit in ("B", "KiB", "MiB", "GiB"):
        if n < 1024:
            return f"{n:.2f} {unit}"
        n /= 1024
    return f"{n:.2f} TiB"


def transcribe(model, processor, audio_path: Path, device: str, dtype) -> dict:
    """Une transcription complète, retourne les métriques de chaque étape."""
    t0 = time.perf_counter()
    inputs = processor.apply_transcription_request(
        language="fr",
        audio=str(audio_path),
        model_id=HF_REPO_ID,
    )
    inputs = inputs.to(device, dtype=dtype)
    t_preprocess = time.perf_counter() - t0

    input_tokens = inputs.input_ids.shape[1]

    t0 = time.perf_counter()
    with torch.no_grad():
        outputs = model.generate(
            **inputs,
            max_new_tokens=1024,
            do_sample=False,
        )
    if torch.cuda.is_available():
        torch.cuda.synchronize()
    t_generate = time.perf_counter() - t0

    output_tokens = outputs.shape[1] - input_tokens

    t0 = time.perf_counter()
    decoded = processor.batch_decode(
        outputs[:, input_tokens:],
        skip_special_tokens=True,
    )[0]
    t_decode = time.perf_counter() - t0

    return {
        "preprocess_s": t_preprocess,
        "generate_s": t_generate,
        "decode_s": t_decode,
        "total_s": t_preprocess + t_generate + t_decode,
        "input_tokens": input_tokens,
        "output_tokens": output_tokens,
        "text": decoded,
    }


def main() -> int:
    device = "cuda"
    dtype = torch.bfloat16

    print(f"[init] torch={torch.__version__}  device={torch.cuda.get_device_name(0)}")
    print()
    print(f"[load] loading model from {MODEL_PATH}")
    t0 = time.perf_counter()
    processor = AutoProcessor.from_pretrained(MODEL_PATH)
    model = VoxtralForConditionalGeneration.from_pretrained(
        MODEL_PATH,
        dtype=dtype,
        device_map=device,
    )
    t_load = time.perf_counter() - t0
    print(f"[load] ready in {t_load:.2f}s, vram allocated={fmt_bytes(torch.cuda.memory_allocated())}")
    print()

    # ── Warm-up jeté ───────────────────────────────────────────────
    warmup_path = CORPUS_DIR / f"{WARMUP_SAMPLE_ID}.wav"
    print(f"[warm] inférence jetée sur {WARMUP_SAMPLE_ID[:8]}... (1.06s)")
    t0 = time.perf_counter()
    _ = transcribe(model, processor, warmup_path, device, dtype)
    print(f"[warm] done in {time.perf_counter() - t0:.2f}s")
    print()

    # ── Mesures ────────────────────────────────────────────────────
    print(f"[meas] {'label':6s}  {'duration':>9s}  {'preproc':>8s}  {'generate':>8s}  {'tokens':>7s}  {'RTF':>6s}")
    print(f"       {'-'*6}  {'-'*9}  {'-'*8}  {'-'*8}  {'-'*7}  {'-'*6}")

    results = []
    for label, duration_s, sid in SAMPLES:
        audio_path = CORPUS_DIR / f"{sid}.wav"
        if not audio_path.exists():
            print(f"[meas] {label}  MISSING: {audio_path}")
            continue

        m = transcribe(model, processor, audio_path, device, dtype)
        rtf = m["total_s"] / duration_s
        print(
            f"[meas] {label}  {duration_s:>8.2f}s  "
            f"{m['preprocess_s']:>7.2f}s  "
            f"{m['generate_s']:>7.2f}s  "
            f"{m['output_tokens']:>7d}  "
            f"{rtf:>5.3f}"
        )
        results.append({
            "label": label.strip(),
            "sample_id": sid,
            "duration_s": duration_s,
            "rtf": rtf,
            "metrics": m,
        })

    print()
    print(f"[vram] peak: {fmt_bytes(torch.cuda.max_memory_allocated())}")
    print()

    print("--- transcriptions ----------------------------------------")
    for r in results:
        print(f"[{r['label']}] {r['metrics']['text']}")
        print()
    print("-----------------------------------------------------------")

    print()
    print(f"[done] RTF range: {min(r['rtf'] for r in results):.3f} - {max(r['rtf'] for r in results):.3f}")
    print(f"[done] reference targets: Whisper ~0.05-0.10, llama-mtmd ~0.05-0.50, viable dictation < 1.0")

    return 0


if __name__ == "__main__":
    sys.exit(main())
