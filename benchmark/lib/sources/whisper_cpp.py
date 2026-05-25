"""Source Whisper.cpp via le binaire whisper-cli.exe.

Wrapper subprocess autour de ``whisper-cli.exe`` (binaire whisper.cpp).
C'est la baseline de référence — la stack de transcription Deckle en
production aujourd'hui.

Le binaire est résolu via :
  1. Argument ``binary=`` du constructeur
  2. Variable d'environnement ``DECKLE_WHISPER_CLI``
  3. Chemin par défaut : ``<repo_root>/whisper.cpp/build/bin/whisper-cli.exe``

Idem pour le modèle (par défaut ``<repo_root>/models/ggml-large-v3.bin``).

Le ``initial_prompt`` Whisper est passé tel quel via ``--prompt`` ; c'est
le ``prompt`` reçu par la méthode ``transcribe()``. Le format de l'output
est forcé à stdout pur texte sans timestamps (``-otxt -of - -nt``).

Note encoding env : sur Windows, MinGW dans le PATH peut faire que
whisper-cli charge des DLL Unix-y au lieu des MSVC runtime DLLs.
``_clean_path_env()`` retire les markers MinGW du PATH pour cet appel.
"""

from __future__ import annotations

import os
import re
import subprocess
import time
from pathlib import Path
from typing import Any

from ._base import Source, Transcription


BENCHMARK_DIR = Path(__file__).resolve().parent.parent.parent
REPO_ROOT     = BENCHMARK_DIR.parent


class WhisperCppSource(Source):
    """Whisper.cpp via whisper-cli.exe."""

    name = "whisper-cpp"
    label = "Whisper.cpp (whisper-cli)"

    def __init__(
        self,
        *,
        binary:   Path | str | None = None,
        model:    Path | str | None = None,
        language: str = "fr",
        extra_args: tuple[str, ...] = (),
    ) -> None:
        self._binary = Path(binary or os.environ.get(
            "DECKLE_WHISPER_CLI", str(_default_binary())))
        self._model = Path(model or os.environ.get(
            "DECKLE_WHISPER_MODEL", str(_default_model())))
        if not self._binary.exists():
            raise FileNotFoundError(
                f"whisper-cli introuvable : {self._binary}\n"
                f"  Définir DECKLE_WHISPER_CLI ou passer binary= au constructeur."
            )
        if not self._model.exists():
            raise FileNotFoundError(
                f"modèle Whisper introuvable : {self._model}\n"
                f"  Définir DECKLE_WHISPER_MODEL ou passer model= au constructeur."
            )
        self.language = language
        self.extra_args = extra_args

    # ── Interface Source ───────────────────────────────────────────────

    def transcribe(
        self,
        *,
        audio_path:     Path,
        prompt:         str,
        max_new_tokens: int | None = None,    # ignoré : whisper-cli n'expose pas
        on_event = None,                       # ignoré
    ) -> Transcription:
        audio_s = _wav_duration_seconds(audio_path)
        cmd: list[str] = [
            str(self._binary),
            "-m",  str(self._model),
            "-l",  self.language,
            "-otxt", "-of", "-",
            "-nt",
        ]
        if prompt:
            cmd.extend(["--prompt", prompt])
        cmd.extend(self.extra_args)
        cmd.append(str(audio_path))

        t0 = time.perf_counter()
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
            return Transcription(
                text="", elapsed_s=0.0, audio_s=audio_s, rtf=0.0,
                generated_tokens=-1, ok=False, error=str(e),
            )
        elapsed = time.perf_counter() - t0

        if proc.returncode != 0:
            tail = (proc.stderr or proc.stdout or "")[-400:]
            return Transcription(
                text="", elapsed_s=elapsed, audio_s=audio_s,
                rtf=elapsed / audio_s if audio_s > 0 else 0.0,
                generated_tokens=-1, ok=False,
                error=f"whisper-cli rc={proc.returncode} : {tail}",
            )

        text = _strip_ansi(proc.stdout).strip()
        return Transcription(
            text=text,
            elapsed_s=elapsed,
            audio_s=audio_s,
            rtf=elapsed / audio_s if audio_s > 0 else float("inf"),
            generated_tokens=-1,    # whisper-cli ne rapporte pas le token count
            ok=True,
            extras={
                "binary":      str(self._binary),
                "model":       str(self._model),
                "language":    self.language,
                "has_prompt":  bool(prompt),
                "prompt_chars": len(prompt),
            },
        )


# ── Helpers locaux ────────────────────────────────────────────────────

def _default_binary() -> Path:
    return (REPO_ROOT / "whisper.cpp" / "build" / "bin" / "whisper-cli.exe").resolve()


def _default_model() -> Path:
    return (REPO_ROOT / "models" / "ggml-large-v3.bin").resolve()


_ANSI_RE = re.compile(r"\x1B\[[0-?]*[ -/]*[@-~]")


def _strip_ansi(s: str) -> str:
    return _ANSI_RE.sub("", s)


_UNIX_PATH_MARKERS = (
    "\\mingw64\\", "\\mingw32\\", "\\msys64\\", "\\msys2\\",
    "\\usr\\bin", "\\usr\\local\\bin",
)


def _clean_path_env() -> dict[str, str]:
    """Retire les répertoires Unix-y du PATH avant de lancer whisper-cli
    pour qu'il charge les DLL MSVC, pas celles de MinGW."""
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
