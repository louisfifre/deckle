"""Sanity check Voxtral Mini 3B BF16 via Transformers + torch ROCm Windows.

Charge le modèle depuis le path local (safetensors shards), prépare la
requête de transcription via le processor officiel, transcrit un sample
court du corpus voxtral-val-30, mesure VRAM et temps.

Usage : exécuter depuis le venv `.venv-voxtral-rocm/` :

    .venv-voxtral-rocm\\Scripts\\python.exe benches\\voxtral-transformers\\sanity_check.py
"""

from __future__ import annotations

import io
import os
import sys
import time
from pathlib import Path

# Force stdout en UTF-8 sous Windows pour ne pas crasher sur les caractères
# non-cp1252 (sortie modèle multilingue, séparateurs box, etc.).
if sys.stdout.encoding.lower() != "utf-8":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8")

import torch
from transformers import AutoProcessor, VoxtralForConditionalGeneration


MODEL_PATH = Path(r"D:\models\llm\voxtral\Voxtral-Mini-3B-2507-safetensors")
HF_REPO_ID = "mistralai/Voxtral-Mini-3B-2507"
CORPUS_DIR = Path(os.environ["LOCALAPPDATA"]) / "Deckle" / "benchmark" / "corpora" / "voxtral-val-30"

# Sample court ~1.7s avec contenu réel ("Et toujours douter un peu.")
SAMPLE_ID = "dcad692a54fd452cbfb174ca9899deba"
SAMPLE_REFERENCE = "Et toujours douter un peu."


def fmt_bytes(n: float) -> str:
    for unit in ("B", "KiB", "MiB", "GiB"):
        if n < 1024:
            return f"{n:.2f} {unit}"
        n /= 1024
    return f"{n:.2f} TiB"


def vram_snapshot() -> str:
    if not torch.cuda.is_available():
        return "no cuda"
    alloc = torch.cuda.memory_allocated()
    reserved = torch.cuda.memory_reserved()
    peak = torch.cuda.max_memory_allocated()
    return (
        f"allocated={fmt_bytes(alloc)}  "
        f"reserved={fmt_bytes(reserved)}  "
        f"peak={fmt_bytes(peak)}"
    )


def main() -> int:
    device = "cuda"
    dtype = torch.bfloat16

    print(f"[init] torch={torch.__version__}")
    print(f"[init] device={torch.cuda.get_device_name(0)}")
    print(f"[init] dtype={dtype}  bf16_supported={torch.cuda.is_bf16_supported()}")

    audio_path = CORPUS_DIR / f"{SAMPLE_ID}.wav"
    if not audio_path.exists():
        print(f"[error] audio sample not found: {audio_path}", file=sys.stderr)
        return 2
    print(f"[init] sample: {audio_path.name}")
    print(f"[init] model : {MODEL_PATH}")
    print()

    t0 = time.perf_counter()
    processor = AutoProcessor.from_pretrained(MODEL_PATH)
    t_processor = time.perf_counter() - t0
    print(f"[load] processor in {t_processor:.2f}s  ({type(processor).__name__})")

    t0 = time.perf_counter()
    model = VoxtralForConditionalGeneration.from_pretrained(
        MODEL_PATH,
        dtype=dtype,
        device_map=device,
    )
    t_model = time.perf_counter() - t0
    print(f"[load] model in {t_model:.2f}s")
    print(f"[load] vram: {vram_snapshot()}")
    print()

    t0 = time.perf_counter()
    inputs = processor.apply_transcription_request(
        language="fr",
        audio=str(audio_path),
        model_id=HF_REPO_ID,
    )
    inputs = inputs.to(device, dtype=dtype)
    t_preprocess = time.perf_counter() - t0
    print(f"[infer] preprocess in {t_preprocess:.2f}s")
    print(f"[infer] input shape : {inputs.input_ids.shape}")
    print(f"[infer] vram: {vram_snapshot()}")

    t0 = time.perf_counter()
    with torch.no_grad():
        outputs = model.generate(
            **inputs,
            max_new_tokens=200,
            do_sample=False,
        )
    t_generate = time.perf_counter() - t0
    print(f"[infer] generate in {t_generate:.2f}s")
    print(f"[infer] vram peak: {vram_snapshot()}")
    print()

    decoded = processor.batch_decode(
        outputs[:, inputs.input_ids.shape[1]:],
        skip_special_tokens=True,
    )

    print("--- transcription ------------------------------------------")
    print(decoded[0])
    print("------------------------------------------------------------")
    print(f"reference: {SAMPLE_REFERENCE!r}")
    print()
    print("[done] sanity check passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
