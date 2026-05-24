"""Whisper baseline (W0) wrapper for the Voxtral POC bench.

Runs whisper.cpp ``whisper-cli.exe`` on one WAV file with the active
Deckle initial prompt and returns the transcription + elapsed time.
Used to compute the W0 baseline against which the 5 Voxtral configs
are compared.

The binary path is configurable via the ``DECKLE_WHISPER_CLI``
environment variable. Default path matches the legacy
``whisper_bench.py`` assumption — ``REPO_ROOT/whisper.cpp/build/bin/
whisper-cli.exe`` — which is typically a symlink or a copy from the
maintainer's external whisper.cpp clone (see
``docs/reference/reference--native-runtime--1.0.md``).

If the binary is missing, ``run`` returns a W0Result with ``ok=False``
and a clear ``error`` message so the bench can record the gap rather
than crash.

Reference : ADR-0011 (POC évaluation Voxtral).
"""

from __future__ import annotations

import os
import re
import subprocess
import time
from dataclasses import dataclass
from pathlib import Path


BENCHMARK_DIR = Path(__file__).resolve().parent.parent
REPO_ROOT     = BENCHMARK_DIR.parent


def _default_binary() -> Path:
    return (REPO_ROOT / "whisper.cpp" / "build" / "bin" / "whisper-cli.exe").resolve()


def _default_model() -> Path:
    return (REPO_ROOT / "models" / "ggml-large-v3.bin").resolve()


@dataclass(frozen=True)
class W0Result:
    text:            str
    elapsed_seconds: float
    audio_seconds:   float
    rtf:             float
    ok:              bool
    error:           str = ""


def run(
    *,
    audio_path:     Path,
    initial_prompt: str = "",
    language:       str = "fr",
    binary:         Path | None = None,
    model:          Path | None = None,
    extra_args:     tuple[str, ...] = (),
) -> W0Result:
    """Transcribe one WAV via whisper-cli.exe and return the W0Result."""

    binary = binary or Path(os.environ.get("DECKLE_WHISPER_CLI", str(_default_binary())))
    model  = model  or Path(os.environ.get("DECKLE_WHISPER_MODEL", str(_default_model())))

    if not binary.exists():
        return W0Result("", 0.0, 0.0, 0.0,
                        ok=False,
                        error=f"whisper-cli introuvable : {binary}\n"
                              f"  Définir DECKLE_WHISPER_CLI pour pointer vers le bon binaire.")
    if not model.exists():
        return W0Result("", 0.0, 0.0, 0.0,
                        ok=False,
                        error=f"modèle Whisper introuvable : {model}\n"
                              f"  Définir DECKLE_WHISPER_MODEL pour pointer vers le bon fichier .bin.")

    cmd: list[str] = [
        str(binary),
        "-m",  str(model),
        "-l",  language,
        "-otxt", "-of", "-",      # text output to stdout
        "-nt",                    # no timestamps
    ]
    if initial_prompt:
        cmd.extend(["--prompt", initial_prompt])
    cmd.extend(extra_args)
    cmd.append(str(audio_path))

    t0 = time.time()
    try:
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
            env=_clean_path_env(),
        )
    except FileNotFoundError as e:
        return W0Result("", 0.0, 0.0, 0.0, ok=False, error=str(e))
    elapsed = time.time() - t0

    if proc.returncode != 0:
        tail = (proc.stderr or proc.stdout or "")[-400:]
        return W0Result("", elapsed, 0.0, 0.0, ok=False,
                        error=f"whisper-cli a échoué (rc={proc.returncode}) : {tail}")

    # whisper-cli writes the transcript to stdout with -of -. Strip out
    # ANSI escapes and trailing whitespace.
    text = _strip_ansi(proc.stdout).strip()

    audio_seconds = _wav_duration_seconds(audio_path)
    rtf = elapsed / audio_seconds if audio_seconds > 0 else float("inf")

    return W0Result(
        text=text,
        elapsed_seconds=elapsed,
        audio_seconds=audio_seconds,
        rtf=rtf,
        ok=True,
    )


# ── Helpers (kept local — not worth promoting to lib/util) ───────────

_ANSI_RE = re.compile(r"\x1B\[[0-?]*[ -/]*[@-~]")


def _strip_ansi(s: str) -> str:
    return _ANSI_RE.sub("", s)


_UNIX_PATH_MARKERS = (
    "\\mingw64\\", "\\mingw32\\", "\\msys64\\", "\\msys2\\",
    "\\usr\\bin", "\\usr\\local\\bin",
)


def _clean_path_env() -> dict[str, str]:
    """Filter MSYS/MinGW paths out of PATH before launching whisper-cli
    so it picks up the Windows MSVC runtime DLLs, not the Unix-y ones —
    same fix as in legacy whisper_bench.py."""
    env = dict(os.environ)
    path = env.get("PATH", "")
    cleaned = ";".join(
        seg for seg in path.split(";")
        if not any(marker in seg for marker in _UNIX_PATH_MARKERS)
    )
    env["PATH"] = cleaned
    return env


def _wav_duration_seconds(path: Path) -> float:
    import wave
    try:
        with wave.open(str(path), "rb") as wf:
            frames = wf.getnframes()
            rate = wf.getframerate()
            return frames / float(rate) if rate else 0.0
    except (wave.Error, EOFError):
        import soundfile as sf
        info = sf.info(str(path))
        return float(info.duration)
