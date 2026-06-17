"""Synthesize French Supertonic-3 samples LOCALLY via pure ONNX Runtime.

Supertonic-3 (Supertone) is French (first-class lang) and pure-ONNX-local (the
`supertonic` package runs 4 .onnx graphs on onnxruntime — no torch, no transformers
at inference). ~99M params, CPU real-time. NOTE: its text frontend maps characters
to raw unicode code points — there is NO expression-tag channel (a literal <laugh>
is spoken, not performed). Arbitrary expressive tags live in Orpheus, not here. We
keep only the M1 (male) voice, the one Louis validated.

Weights auto-download once to D:\\models\\tts\\supertonic-3 (outside the repo).
Outputs land in the gitignored run dir.

Run with the dedicated venv:
    benchmark\\.venv-tts-onnx\\Scripts\\python.exe benchmark\\benches\\tts-audition\\supertonic_synth.py
"""

from __future__ import annotations

import sys
import time
import traceback
import wave
from pathlib import Path

import supertonic

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import _harness  # noqa: E402

OUT = _harness.RUN_DIR
MODEL_DIR = Path(r"D:\models\tts\supertonic-3")

VOICES = ["M1"]  # keeper — Louis validated the M1 (male) voice; F5/F1 dropped as noise.


def log(m: str) -> None:
    print(m, flush=True)


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    log(f"Output: {OUT}\nModel dir: {MODEL_DIR}")

    # Public neutral set + real corpus dictation (read at runtime, never versioned).
    # No tag sentences: Supertonic has no tag channel (it would speak "<laugh>").
    sentences = {**_harness.PUBLIC_SENTENCES, **_harness.corpus_sentences()}

    log("Loading Supertonic-3 (auto-download on first run)…")
    t0 = time.perf_counter()
    tts = supertonic.TTS(model="supertonic-3", model_dir=str(MODEL_DIR), auto_download=True)
    load_s = time.perf_counter() - t0
    log(f"Loaded in {load_s:.1f}s.")

    for voice in VOICES:
        log(f"\n== voice {voice} ==")
        try:
            style = tts.get_voice_style(voice)
        except Exception as e:  # noqa: BLE001
            log(f"  voice style FAILED: {type(e).__name__}: {str(e)[:160]}")
            continue
        comp = aud = 0.0
        n = 0
        for sid, text in sentences.items():
            dest = OUT / f"onnx_supertonic_{voice}_{sid}.wav"
            try:
                tc = time.perf_counter()
                wav, _ = tts.synthesize(text=text, voice_style=style,
                                        total_steps=16, speed=1.0, lang="fr")
                comp += time.perf_counter() - tc
                tts.save_audio(wav, str(dest))
                with wave.open(str(dest)) as w:
                    secs = w.getnframes() / w.getframerate()
                aud += secs
                n += 1
                log(f"  wrote {dest.name}  ({secs:.1f}s)")
            except Exception as e:  # noqa: BLE001
                log(f"  GEN FAILED {sid}: {type(e).__name__}: {str(e)[:160]}")
        if n:
            _harness.stats_record(
                "supertonic", voice, ep="cpu", n=n,
                compute_s=round(comp, 1), audio_s=round(aud, 1),
                rtf=round(aud / comp, 1) if comp else None, load_s=round(load_s, 1))

    wavs = sorted(OUT.glob("onnx_supertonic_*.wav"))
    log(f"\nDone. {len(wavs)} Supertonic wavs:")
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
