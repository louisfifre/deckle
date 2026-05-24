"""Smoke test Voxtral — charge le modèle et transcrit un WAV de test.

Valide en bout-en-bout que la stack ROCm + transformers + Voxtral tient
debout sur la machine avant d'engager l'écriture du bench complet. Si ce
script tourne et sort une transcription cohérente du speech.wav warm-up,
voxtral_bench.py peut être écrit avec confiance.

Le préfixe ``_`` exclut ce fichier de la découverte du launcher (cf.
AGENT.md) — c'est un smoke test ponctuel, pas un bench récurrent.

Usage :
    .\\.venv-voxtral\\Scripts\\python.exe _voxtral_smoke.py
    .\\.venv-voxtral\\Scripts\\python.exe _voxtral_smoke.py --audio path\\to\\other.wav
    .\\.venv-voxtral\\Scripts\\python.exe _voxtral_smoke.py --cpu  # forcer CPU
"""

from __future__ import annotations

import argparse
import io
import sys
import time
from pathlib import Path

# UTF-8 stdout/stderr sur Windows pour les accents français.
if sys.stdout.encoding != "utf-8":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
if sys.stderr.encoding != "utf-8":
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")

BENCHMARK_DIR = Path(__file__).resolve().parent
REPO_ROOT = BENCHMARK_DIR.parent

# Warm-up clip embarqué dans Deckle.App (PCM mono 16-bit 16 kHz, ~1.6 s).
DEFAULT_AUDIO = REPO_ROOT / "src" / "Deckle.App" / "Assets" / "Sounds" / "speech.wav"

MODEL_ID = "mistralai/Voxtral-Mini-3B-2507"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--audio", type=Path, default=DEFAULT_AUDIO,
                        help=f"Chemin du WAV à transcrire (défaut : {DEFAULT_AUDIO})")
    parser.add_argument("--language", default="fr",
                        help="Code langue pour la transcription (défaut : fr)")
    parser.add_argument("--max-new-tokens", type=int, default=500,
                        help="Plafond de tokens en génération (défaut : 500)")
    parser.add_argument("--cpu", action="store_true",
                        help="Forcer l'inférence CPU même si GPU dispo")
    args = parser.parse_args()

    if not args.audio.exists():
        print(f"FATAL: audio introuvable — {args.audio}", file=sys.stderr)
        sys.exit(1)

    print(f"=== Voxtral smoke test ===")
    print(f"  Modèle : {MODEL_ID}")
    print(f"  Audio  : {args.audio} ({args.audio.stat().st_size} octets)")
    print()

    # Import torch tard pour donner un message d'erreur lisible si le venv
    # n'est pas le bon.
    try:
        import torch
        from transformers import AutoProcessor, VoxtralForConditionalGeneration
    except ImportError as e:
        print(f"FATAL: import échoué — {e}", file=sys.stderr)
        print("       Vérifier que .venv-voxtral est activé et que setup-voxtral-env.ps1 a tourné.",
              file=sys.stderr)
        sys.exit(2)

    # ── Détection device ────────────────────────────────────────────────
    if args.cpu or not torch.cuda.is_available():
        device = "cpu"
        dtype = torch.float32
        device_name = "CPU"
    else:
        device = "cuda"
        dtype = torch.bfloat16
        device_name = torch.cuda.get_device_name(0)

    print(f"  Device : {device_name} ({device}, dtype={dtype})")
    hip = getattr(torch.version, "hip", None)
    if hip:
        print(f"  HIP    : {hip}")
    print()

    # ── Chargement modèle ───────────────────────────────────────────────
    t0 = time.time()
    print("[1/3] Chargement processor + modèle…", flush=True)
    processor = AutoProcessor.from_pretrained(MODEL_ID)
    model = VoxtralForConditionalGeneration.from_pretrained(
        MODEL_ID,
        torch_dtype=dtype,
        device_map=device,
    )
    print(f"      OK en {time.time() - t0:.1f}s")
    print()

    # ── Préparation requête transcription ───────────────────────────────
    t1 = time.time()
    print("[2/3] Préparation de la requête transcription…", flush=True)
    inputs = processor.apply_transcrition_request(
        language=args.language,
        audio=str(args.audio),
        model_id=MODEL_ID,
    )
    inputs = inputs.to(device, dtype=dtype)
    print(f"      OK en {time.time() - t1:.1f}s")
    print(f"      input_ids shape : {tuple(inputs.input_ids.shape)}")
    print()

    # ── Génération ──────────────────────────────────────────────────────
    t2 = time.time()
    print(f"[3/3] Génération (max_new_tokens={args.max_new_tokens})…", flush=True)
    with torch.inference_mode():
        outputs = model.generate(**inputs, max_new_tokens=args.max_new_tokens)
    gen_elapsed = time.time() - t2
    print(f"      OK en {gen_elapsed:.1f}s ({outputs.shape[1] - inputs.input_ids.shape[1]} tokens)")
    print()

    # ── Décodage ────────────────────────────────────────────────────────
    decoded = processor.batch_decode(
        outputs[:, inputs.input_ids.shape[1]:],
        skip_special_tokens=True,
    )

    print("─" * 70)
    print("TRANSCRIPTION :")
    print("─" * 70)
    for i, text in enumerate(decoded):
        print(text)
    print("─" * 70)
    print()
    print(f"✓ Smoke test terminé. Total : {time.time() - t0:.1f}s")


if __name__ == "__main__":
    main()
