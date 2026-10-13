"""Voxtral through ``llama-mtmd-cli`` (llama.cpp, Vulkan), a GGUF plus its
audio ``mmproj``.

A subprocess, because llama.cpp offers no stable multimodal Python binding
for libmtmd; the CLI is the stable surface. The isolated process also lets
the bench survive a Voxtral crash (OOM, Vulkan segfault) as an ``ok=False``
row.

The default GGUF is a Q4_K_M, for smoke tests of the llama.cpp/Vulkan stack
only: Q4 is neither a quality validation nor a shipping candidate. A non-Q4
model passed explicitly gets the source's normal identity.

``system_prompt`` is an extra keyword the base ``Source`` lacks; ``None``
leaves ``--system-prompt`` off the command line. ``--n-gpu-layers 99`` puts
every layer on the GPU. The CLI logs on stderr, so stdout is the transcript.
"""

from __future__ import annotations

import math
import os
import re
import subprocess
import time
from pathlib import Path
from typing import Any, Callable

from ._base import Source, Transcription


# Under git-bash or MinGW, PATH holds folders whose DLLs are ABI-incompatible
# with MSVC binaries: the Windows loader picks one and the process dies with
# ``STATUS_ENTRYPOINT_NOT_FOUND`` (0xC0000139) before printing anything.
# These folders are dropped from PATH before the launch.
_UNIX_PATH_MARKERS = (
    "\\mingw64\\", "\\mingw32\\", "\\msys64\\", "\\msys2\\",
    "\\usr\\bin", "\\usr\\local\\bin",
)


from lib import paths


VOXTRAL_DIR = paths.MODELS_DIR / "voxtral"


class VoxtralLlamacppSource(Source):
    name = "voxtral-llamacpp"
    label = "Voxtral via llama-mtmd-cli (Vulkan)"

    def __init__(
        self,
        *,
        binary:    Path | str | None = None,
        model:     Path | str | None = None,
        mmproj:    Path | str | None = None,
        n_gpu_layers: int  = 99,
        ctx_size:     int  = 4096,
        temperature:  float = 0.0,
        n_predict_floor: int = 128,
        n_predict_per_audio_s: float = 4.0,
    ) -> None:
        self._binary = Path(binary or os.environ.get(
            "DECKLE_LLAMA_MTMD_CLI",
            r"D:\workspace\llama.cpp\build\bin\llama-mtmd-cli.exe"))
        self._model = Path(model or os.environ.get(
            "DECKLE_VOXTRAL_MODEL",
            str(VOXTRAL_DIR / "Voxtral-Small-24B-2507-Q4_K_M.gguf")))
        self._mmproj = Path(mmproj or os.environ.get(
            "DECKLE_VOXTRAL_MMPROJ",
            str(VOXTRAL_DIR / "mmproj-Voxtral-Small-24B-2507.gguf")))

        self.smoke_only = _is_q4_model(self._model)
        if self.smoke_only:
            self.name = "voxtral-llamacpp-q4-smoke"
            self.label = "Voxtral via llama-mtmd-cli (Vulkan, Q4 smoke only)"

        for label, p in (("binary", self._binary), ("model", self._model),
                         ("mmproj", self._mmproj)):
            if not p.exists():
                raise FileNotFoundError(
                    f"voxtral-llamacpp {label} introuvable : {p}\n"
                    f"  Définir DECKLE_LLAMA_MTMD_CLI / DECKLE_VOXTRAL_MODEL / "
                    f"DECKLE_VOXTRAL_MMPROJ ou passer au constructeur."
                )

        self.n_gpu_layers          = n_gpu_layers
        self.ctx_size              = ctx_size
        self.temperature           = temperature
        self.n_predict_floor       = n_predict_floor
        self.n_predict_per_audio_s = n_predict_per_audio_s

    # ── Source interface ───────────────────────────────────────────────

    def transcribe(
        self,
        *,
        audio_path:     Path,
        prompt:         str,
        max_new_tokens: int | None = None,
        on_event:       Callable[[str, dict], None] | None = None,
        system_prompt:  str | None = None,
    ) -> Transcription:
        audio_s = _wav_duration_seconds(audio_path)
        n_predict = max_new_tokens or max(
            self.n_predict_floor,
            int(math.ceil(audio_s * self.n_predict_per_audio_s)),
        )

        cmd: list[str] = [
            str(self._binary),
            "--model",         str(self._model),
            "--mmproj",        str(self._mmproj),
            "--audio",         str(audio_path),
            "--n-gpu-layers",  str(self.n_gpu_layers),
            "--ctx-size",      str(self.ctx_size),
            "--n-predict",     str(n_predict),
            "--temp",          f"{self.temperature:.3f}",
            "--prompt",        prompt,
        ]
        if system_prompt:
            cmd.extend(["--system-prompt", system_prompt])

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
                error=f"llama-mtmd-cli rc={proc.returncode} : {tail}",
            )

        text   = _strip_ansi(proc.stdout).strip()
        tokens = _extract_eval_tokens(proc.stderr)

        return Transcription(
            text=text,
            elapsed_s=elapsed,
            audio_s=audio_s,
            rtf=elapsed / audio_s if audio_s > 0 else float("inf"),
            generated_tokens=tokens,
            ok=True,
            extras={
                "binary":          str(self._binary),
                "model":           str(self._model),
                "mmproj":          str(self._mmproj),
                "evaluation_scope": "smoke" if self.smoke_only else "model-defined",
                "n_gpu_layers":    self.n_gpu_layers,
                "ctx_size":        self.ctx_size,
                "n_predict":       n_predict,
                "temperature":     self.temperature,
                "has_system":      bool(system_prompt),
                "user_prompt_chars": len(prompt),
                "system_prompt_chars": len(system_prompt or ""),
            },
        )


# ── Helpers ───────────────────────────────────────────────────────────

_ANSI_RE = re.compile(r"\x1B\[[0-?]*[ -/]*[@-~]")

_Q4_MODEL_RE = re.compile(r"(?:^|[-_.])(?:Q4|Q4F16|INT4)(?:[-_.]|$)", re.IGNORECASE)


def _is_q4_model(path: Path) -> bool:
    return _Q4_MODEL_RE.search(path.stem) is not None


def _strip_ansi(s: str) -> str:
    return _ANSI_RE.sub("", s)


# llama.cpp prints on stderr a line such as
# ``eval time =    1234.56 ms /   123 tokens (   10.04 ms per token,  99.5 tokens per second)``
_EVAL_TOKENS_RE = re.compile(
    r"eval time\s*=\s*[\d.]+\s*ms\s*/\s*(\d+)\s*tokens",
    re.IGNORECASE,
)


def _extract_eval_tokens(stderr: str) -> int:
    if not stderr:
        return -1
    m = _EVAL_TOKENS_RE.search(stderr)
    if not m:
        return -1
    try:
        return int(m.group(1))
    except (ValueError, IndexError):
        return -1


def _wav_duration_seconds(path: Path) -> float:
    import wave
    try:
        with wave.open(str(path), "rb") as wf:
            frames = wf.getnframes()
            rate   = wf.getframerate()
            return frames / float(rate) if rate else 0.0
    except (wave.Error, EOFError):
        import soundfile as sf
        info = sf.info(str(path))
        return float(info.duration)


def _clean_path_env() -> dict[str, str]:
    env = dict(os.environ)
    path = env.get("PATH", "")
    cleaned = ";".join(
        seg for seg in path.split(";")
        if not any(marker in seg for marker in _UNIX_PATH_MARKERS)
    )
    env["PATH"] = cleaned
    return env
