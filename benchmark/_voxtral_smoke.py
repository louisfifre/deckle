"""Smoke test Voxtral — exécute llama-mtmd-cli sur un WAV de test.

Valide la stack llama.cpp + Vulkan + GGUF Voxtral + libmtmd en
bout-en-bout avant que voxtral_bench.py soit câblé dessus. Si ce
script tourne et sort une transcription cohérente du speech.wav
warm-up, voxtral_bench.py peut être exécuté avec confiance.

Le préfixe ``_`` exclut ce fichier de la découverte du launcher.

Usage :
    .\\.venv-voxtral\\Scripts\\python.exe _voxtral_smoke.py
    .\\.venv-voxtral\\Scripts\\python.exe _voxtral_smoke.py --audio path\\to\\other.wav
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
REPO_ROOT     = BENCHMARK_DIR.parent
sys.path.insert(0, str(BENCHMARK_DIR))

from lib.voxtral_engine import VoxtralEngine  # noqa: E402

DEFAULT_AUDIO = REPO_ROOT / "src" / "Deckle.App" / "Assets" / "Sounds" / "speech.wav"

DEFAULT_PROMPT = (
    "Tu es un transcripteur audio professionnel. Transcris l'audio en "
    "français en respectant la ponctuation et les accents standards. "
    "Sors uniquement le texte transcrit, rien d'autre."
)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--audio",          type=Path, default=DEFAULT_AUDIO,
                        help=f"Chemin du WAV (défaut : {DEFAULT_AUDIO})")
    parser.add_argument("--prompt",         default=DEFAULT_PROMPT,
                        help="System prompt à passer (défaut : prompt lissé court)")
    parser.add_argument("--max-new-tokens", type=int, default=500,
                        help="Plafond de tokens en génération (défaut : 500)")
    args = parser.parse_args()

    if not args.audio.exists():
        print(f"FATAL: audio introuvable — {args.audio}", file=sys.stderr)
        sys.exit(1)

    print("=== Voxtral smoke test (llama.cpp/Vulkan/GGUF) ===")
    print(f"  Audio  : {args.audio} ({args.audio.stat().st_size} octets)")
    print(f"  Prompt : {args.prompt[:80]}{'…' if len(args.prompt) > 80 else ''}")
    print()

    try:
        engine = VoxtralEngine()
    except FileNotFoundError as e:
        print(f"FATAL: {e}", file=sys.stderr)
        sys.exit(2)

    t0 = time.time()
    print("Transcription en cours via llama-mtmd-cli…", flush=True)
    result = engine.transcribe(
        audio_path=args.audio,
        config_name="smoke",
        system_prompt=args.prompt,
        max_new_tokens=args.max_new_tokens,
    )
    elapsed = time.time() - t0

    if not result.ok:
        print(f"FAIL: {result.error}", file=sys.stderr)
        sys.exit(3)

    print(f"OK en {elapsed:.1f}s (audio {result.audio_seconds:.2f}s, RTF {result.rtf:.2f})")
    print()
    print("─" * 70)
    print("TRANSCRIPTION :")
    print("─" * 70)
    print(result.text)
    print("─" * 70)


if __name__ == "__main__":
    main()
