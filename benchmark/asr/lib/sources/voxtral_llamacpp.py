"""Source Voxtral via le binaire ``llama-mtmd-cli`` (stack Vulkan).

Wrapper subprocess autour de ``llama-mtmd-cli.exe`` — le CLI multimodal
de ``llama.cpp`` qui charge un GGUF Voxtral et son ``mmproj`` audio,
puis traite un fichier audio + un prompt textuel pour produire une
transcription. C'est la voie qui a été validée perf en session
2026-05-26 (730 GB/s sur Q4_K_M 24B, 47 tok/s, RTF 0.05 sur sample 12s).

Pourquoi un wrapper subprocess et pas la lib Python directe : ``llama.cpp``
n'expose pas de binding Python multimodal stable (pas d'équivalent à
``llama-cpp-python`` pour libmtmd à date). Le CLI est la surface stable.
Side-effect appréciable : process isolé → si Voxtral plante (OOM,
segfault Vulkan), le bench survit avec un row ``ok=False`` au lieu de
mourir.

Le contrat ``Source`` est respecté. La méthode ``transcribe()`` accepte
deux prompts via kwargs :
  - ``prompt`` : l'instruction utilisateur transmise via ``--prompt``.
  - ``system_prompt`` : système Mistral V7 transmis via
    ``--system-prompt`` (optionnel ; ``None`` => pas de ``--system-prompt``
    sur la ligne de commande). Ce param est documenté dans le contrat
    ``Source`` comme kwarg optionnel — les sources qui ne le supportent
    pas l'ignorent.

Paramètres figés (cohérents avec ``perf-cap/session-2026-05-26-prompts.ps1``
qui a validé la stack) : ``--n-gpu-layers 99`` (tous les layers sur GPU
Vulkan), ``--ctx-size 4096`` (large marge vs les ~1320 tokens max
attendus à 330s × 4), ``--temp 0.0`` (déterminisme pour reproductibilité
bench). Le ``--n-predict`` est adaptatif : ``max(128, ceil(audio_s * 4))``
pour absorber les longs sans tronquer.

Extraction de la transcription : stdout pure (le CLI envoie ses logs
sur stderr). Strip ANSI au passage. Le compte de tokens générés est
extrait du stderr via parsing de la ligne ``eval time = ... ms / N
tokens / ...`` si présente — sinon on retourne ``-1``.

Référence :
  - ``perf-cap/session-2026-05-26-prompts.ps1`` (régimes T1-T6 testés)
  - Brief session 2026-05-26 (validation perf Q4_K_M)
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


# Markers de chemins Unix-y à retirer du PATH avant de lancer le binaire.
# Sous git-bash/MinGW sur Windows, le PATH contient des dossiers qui
# exposent des DLL ABI-incompatibles avec les binaires MSVC : le loader
# Windows pioche dans l'une d'elles et le process se termine avec
# ``STATUS_ENTRYPOINT_NOT_FOUND`` (0xC0000139) avant d'imprimer quoi
# que ce soit. Même pathologie que ``whisper_cpp.py``.
_UNIX_PATH_MARKERS = (
    "\\mingw64\\", "\\mingw32\\", "\\msys64\\", "\\msys2\\",
    "\\usr\\bin", "\\usr\\local\\bin",
)


from lib import paths


VOXTRAL_DIR = paths.MODELS_DIR / "voxtral"
"""ASR-local convention for Voxtral GGUF + mmproj files."""


class VoxtralLlamacppSource(Source):
    """Voxtral via ``llama-mtmd-cli`` (Vulkan)."""

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
        # Paths centralisés via benchmark/lib/paths.py — les GGUF Voxtral
        # vivent sous D:\models\llm\voxtral\ par défaut.
        # Override par kwarg, sinon par env var, sinon défaut paths.
        self._binary = Path(binary or os.environ.get(
            "DECKLE_LLAMA_MTMD_CLI",
            r"D:\workspace\llama.cpp\build\bin\llama-mtmd-cli.exe"))
        self._model = Path(model or os.environ.get(
            "DECKLE_VOXTRAL_MODEL",
            str(VOXTRAL_DIR / "Voxtral-Small-24B-2507-Q4_K_M.gguf")))
        self._mmproj = Path(mmproj or os.environ.get(
            "DECKLE_VOXTRAL_MMPROJ",
            str(VOXTRAL_DIR / "mmproj-Voxtral-Small-24B-2507.gguf")))

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

    # ── Interface Source ───────────────────────────────────────────────

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
                "n_gpu_layers":    self.n_gpu_layers,
                "ctx_size":        self.ctx_size,
                "n_predict":       n_predict,
                "temperature":     self.temperature,
                "has_system":      bool(system_prompt),
                "user_prompt_chars": len(prompt),
                "system_prompt_chars": len(system_prompt or ""),
            },
        )


# ── Helpers libres ────────────────────────────────────────────────────

_ANSI_RE = re.compile(r"\x1B\[[0-?]*[ -/]*[@-~]")


def _strip_ansi(s: str) -> str:
    return _ANSI_RE.sub("", s)


# llama.cpp imprime sur stderr une ligne du style :
# ``eval time =    1234.56 ms /   123 tokens (   10.04 ms per token,  99.5 tokens per second)``
# On capture le compte de tokens ; si non trouvé, -1 (la source ne le
# rapporte pas, conforme au contrat).
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
    """Retire les dossiers Unix-y du PATH avant de lancer le binaire,
    pour qu'il charge les DLL MSVC et pas celles de MinGW. Pattern
    identique à ``whisper_cpp._clean_path_env`` (qui résout la même
    pathologie sur ``whisper-cli.exe``)."""
    env = dict(os.environ)
    path = env.get("PATH", "")
    cleaned = ";".join(
        seg for seg in path.split(";")
        if not any(marker in seg for marker in _UNIX_PATH_MARKERS)
    )
    env["PATH"] = cleaned
    return env
