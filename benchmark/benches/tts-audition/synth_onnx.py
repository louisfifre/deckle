"""Synthesize French TTS samples LOCALLY via pure ONNX Runtime (sherpa-onnx).

This is the on-doctrine path: no online inference, no `transformers` — weights are
downloaded from the sherpa-onnx model releases (prebuilt .onnx bundles) and run
through sherpa-onnx's ONNX Runtime pipeline on the CPU EP. The VOICE is identical
to any other runtime; CPU vs DirectML only changes latency, which is the separate
deployment question.

Each bundle is auto-detected from its extracted contents (model .onnx, tokens,
espeak-ng-data, voices.bin / dict / lexicon for Kokoro), so we don't hardcode
inner filenames. Weights live under D:\\models\\tts\\ (outside the repo, like the
ASR ONNX models under D:\\models\\llm\\). Outputs land in the gitignored run dir.

Run with the dedicated venv:
    benchmark\\.venv-tts-onnx\\Scripts\\python.exe benchmark\\benches\\tts-audition\\synth_onnx.py
"""

from __future__ import annotations

import sys
import tarfile
import time
import traceback
import urllib.request
from pathlib import Path

import numpy as np
import soundfile as sf
import sherpa_onnx

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import _harness  # noqa: E402

OUT = _harness.RUN_DIR
MODELS = Path(r"D:\models\tts")
RELEASE = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models"

# Keeper voices only — Louis validated Piper UPMC (jessica=s0, pierre=s1). siwis,
# tom and Meta MMS were dropped from the audition as noise. Sherpa-onnx is the
# pure-ONNX path (CPU EP; VITS is tiny and already far faster than real-time, so
# DirectML buys nothing here).
MODELS_REGISTRY = [
    {"key": "piper_upmc", "label": "Piper FR upmc (VITS, 2 voix)",
     "tar": "vits-piper-fr_FR-upmc-medium.tar.bz2", "sids": [0, 1]},
]


def log(m: str) -> None:
    print(m, flush=True)


def fetch(tar_name: str) -> Path | None:
    """Download + extract a sherpa-onnx model bundle; return its dir. Skip if present."""
    stem = tar_name.replace(".tar.bz2", "")
    dest = MODELS / stem
    if dest.is_dir() and any(dest.glob("*.onnx")):
        log(f"  cached: {stem}")
        return dest
    MODELS.mkdir(parents=True, exist_ok=True)
    url = f"{RELEASE}/{tar_name}"
    tar_path = MODELS / tar_name
    log(f"  downloading {tar_name}")
    try:
        urllib.request.urlretrieve(url, tar_path)
    except Exception as e:  # noqa: BLE001
        log(f"    DOWNLOAD FAILED: {type(e).__name__}: {str(e)[:160]}")
        return None
    log(f"  extracting {tar_name}")
    with tarfile.open(tar_path, "r:bz2") as t:
        t.extractall(MODELS)
    tar_path.unlink(missing_ok=True)
    return dest if dest.is_dir() else None


def build_tts(d: Path) -> sherpa_onnx.OfflineTts:
    """Auto-detect the model type from the extracted bundle and build OfflineTts."""
    onnx_files = sorted(p for p in d.glob("*.onnx"))
    tokens = d / "tokens.txt"
    espeak = d / "espeak-ng-data"
    voices = d / "voices.bin"
    dict_dir = d / "dict"
    lexicons = ",".join(str(p) for p in sorted(d.glob("lexicon*.txt")))
    data_dir = str(espeak) if espeak.is_dir() else ""

    if voices.exists():  # Kokoro
        model = str(next((p for p in onnx_files if p.name == "model.onnx"), onnx_files[0]))
        kokoro = sherpa_onnx.OfflineTtsKokoroModelConfig(
            model=model, voices=str(voices), tokens=str(tokens),
            data_dir=data_dir, dict_dir=str(dict_dir) if dict_dir.is_dir() else "",
            lexicon=lexicons)
        mc = sherpa_onnx.OfflineTtsModelConfig(kokoro=kokoro, num_threads=2, provider="cpu")
    else:  # VITS (Piper / MMS)
        model = str(next((p for p in onnx_files if p.name != "voices.bin"), onnx_files[0]))
        vits = sherpa_onnx.OfflineTtsVitsModelConfig(
            model=model, tokens=str(tokens), data_dir=data_dir,
            lexicon=lexicons if lexicons else "")
        mc = sherpa_onnx.OfflineTtsModelConfig(vits=vits, num_threads=2, provider="cpu")

    return sherpa_onnx.OfflineTts(sherpa_onnx.OfflineTtsConfig(model=mc, max_num_sentences=1))


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    log(f"Output: {OUT}\nModels: {MODELS}")

    # Public neutral set + real corpus dictation (read at runtime, never versioned).
    sentences = {**_harness.PUBLIC_SENTENCES, **_harness.corpus_sentences()}

    for spec in MODELS_REGISTRY:
        log(f"\n== {spec['label']} ==")
        d = fetch(spec["tar"])
        if d is None:
            log("  -> skipped (no bundle)")
            continue
        t0 = time.perf_counter()
        try:
            tts = build_tts(d)
        except Exception as e:  # noqa: BLE001
            log(f"  BUILD FAILED: {type(e).__name__}: {str(e)[:200]}")
            continue
        load_s = time.perf_counter() - t0
        n_spk = tts.num_speakers
        sids = [s for s in spec["sids"] if s < max(n_spk, 1)] or [0]
        log(f"  speakers={n_spk}  sids={sids}  load={load_s:.1f}s")
        for sid in sids:
            suffix = f"_s{sid}" if len(sids) > 1 else ""
            comp = aud = 0.0
            n = 0
            for sentence_id, text in sentences.items():
                dest = OUT / f"onnx_{spec['key']}{suffix}_{sentence_id}.wav"
                try:
                    tc = time.perf_counter()
                    audio = tts.generate(text, sid=sid, speed=1.0)
                    comp += time.perf_counter() - tc
                    secs = len(audio.samples) / audio.sample_rate
                    aud += secs
                    n += 1
                    sf.write(str(dest), np.asarray(audio.samples, dtype=np.float32),
                             audio.sample_rate)
                    log(f"    wrote {dest.name}  ({audio.sample_rate} Hz, {secs:.1f}s)")
                except Exception as e:  # noqa: BLE001
                    log(f"    GEN FAILED {sentence_id}: {type(e).__name__}: {str(e)[:160]}")
            if n:
                _harness.stats_record(
                    spec["key"], f"s{sid}", ep="cpu", n=n,
                    compute_s=round(comp, 1), audio_s=round(aud, 1),
                    rtf=round(aud / comp, 1) if comp else None, load_s=round(load_s, 1))

    wavs = sorted(OUT.glob("onnx_piper_*.wav"))
    log(f"\nDone. {len(wavs)} Piper wavs:")
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
