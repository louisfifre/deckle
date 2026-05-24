"""Voxtral inference wrapper for the POC bench (llama.cpp/Vulkan pivot).

Wrapper subprocess autour de ``llama-mtmd-cli`` (binaire llama.cpp avec
backend Vulkan + libmtmd pour le multimodal audio). Analogue à
``voxtral_baseline_whisper.py`` qui wrap ``whisper-cli.exe`` — même
pattern, même structure.

Le chemin du binaire et des GGUF est lu depuis
``benchmark/config/voxtral_paths.toml`` (généré par
``setup-voxtral-env.ps1``). Override via env vars :
  - DECKLE_LLAMA_MTMD_CLI
  - DECKLE_VOXTRAL_GGUF
  - DECKLE_VOXTRAL_MMPROJ

Le système prompt pilote le régime (V1 raw / V2 lissé / V3 fidèle /
V4 fidèle annoté / V5 traduit EN) — comme via transformers, mais ici
c'est passé via le format prompt llama-mtmd-cli.

Référence : ADR-0011 (POC évaluation Voxtral), section "Pivot stack".
"""

from __future__ import annotations

import os
import re
import subprocess
import time
import tomllib
from dataclasses import dataclass
from pathlib import Path


BENCHMARK_DIR = Path(__file__).resolve().parent.parent
PATHS_TOML    = BENCHMARK_DIR / "config" / "voxtral_paths.toml"


@dataclass(frozen=True)
class VoxtralResult:
    text:             str
    elapsed_seconds:  float
    rtf:              float
    audio_seconds:    float
    config_name:      str
    ok:               bool
    error:            str = ""
    # Tokens générés. -1 quand llama-mtmd-cli ne le rapporte pas en stdout
    # (la version actuelle ne le fait pas — on garde le champ pour
    # compatibilité avec le bench qui peut le sérialiser tel quel).
    generated_tokens: int = -1


def _load_paths() -> tuple[Path, Path, Path]:
    """Lit les 3 chemins du config TOML (avec override env vars)."""
    cli_env    = os.environ.get("DECKLE_LLAMA_MTMD_CLI")
    gguf_env   = os.environ.get("DECKLE_VOXTRAL_GGUF")
    mmproj_env = os.environ.get("DECKLE_VOXTRAL_MMPROJ")

    if cli_env and gguf_env and mmproj_env:
        return Path(cli_env), Path(gguf_env), Path(mmproj_env)

    if not PATHS_TOML.exists():
        raise FileNotFoundError(
            f"Config chemins absente : {PATHS_TOML}\n"
            f"  Lancer setup-voxtral-env.ps1 pour la générer, ou définir "
            f"DECKLE_LLAMA_MTMD_CLI / DECKLE_VOXTRAL_GGUF / DECKLE_VOXTRAL_MMPROJ."
        )
    with PATHS_TOML.open("rb") as f:
        data = tomllib.load(f)
    p = data["paths"]
    return (
        Path(cli_env    or p["llama_mtmd_cli"]),
        Path(gguf_env   or p["voxtral_gguf"]),
        Path(mmproj_env or p["voxtral_mmproj"]),
    )


class VoxtralEngine:
    """Façade compatible avec l'ancienne API basée sur transformers.

    L'API publique reste ``transcribe(audio_path, config_name, system_prompt,
    language)`` pour que voxtral_bench.py n'ait rien à changer côté
    invocation. Le device retourné est ``"vulkan"`` (informatif).
    """

    def __init__(self, **_ignored_kwargs) -> None:
        # **_ignored_kwargs absorbe les anciens kwargs (device, dtype, etc.)
        # de la version transformers pour ne pas casser les callers existants.
        self._cli, self._gguf, self._mmproj = _load_paths()
        for path, label in (
            (self._cli, "llama-mtmd-cli"),
            (self._gguf, "GGUF Voxtral"),
            (self._mmproj, "mmproj"),
        ):
            if not path.exists():
                raise FileNotFoundError(f"{label} introuvable : {path}")
        self._load_seconds: float | None = None

    @property
    def device(self) -> str:
        return "vulkan"

    @property
    def load_seconds(self) -> float | None:
        return self._load_seconds

    def transcribe(
        self,
        *,
        audio_path:      Path,
        config_name:     str,
        system_prompt:   str,
        language:        str   = "fr",
        max_new_tokens:  int   = 2000,
    ) -> VoxtralResult:
        """Run one transcription via llama-mtmd-cli subprocess.

        Le system prompt est combiné avec une instruction de transcription
        explicite, parce que mtmd-cli prend un `-p` unique (pas de
        séparation system/user). Le langage est passé seulement comme hint
        textuel dans le prompt.
        """
        audio_seconds = _wav_duration_seconds(audio_path)

        # On compose une instruction unique. La consigne Voxtral système
        # est mise en haut, suivie d'une mention explicite du fichier
        # audio à transcrire — le tokenizer audio sait l'extraire via
        # le flag --audio.
        prompt = system_prompt.strip()

        cmd = [
            str(self._cli),
            "-m",        str(self._gguf),
            "--mmproj",  str(self._mmproj),
            "--audio",   str(audio_path),
            "-p",        prompt,
            "-n",        str(max_new_tokens),
            "--no-display-prompt",   # ne pas rééchoer le prompt dans stdout
            "-ngl",      "999",      # tout sur GPU
        ]

        t0 = time.time()
        try:
            proc = subprocess.run(
                cmd,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                check=False,
            )
        except FileNotFoundError as e:
            return VoxtralResult("", 0.0, 0.0, audio_seconds, config_name,
                                 ok=False, error=str(e))
        elapsed = time.time() - t0

        if proc.returncode != 0:
            tail = (proc.stderr or proc.stdout or "")[-500:]
            return VoxtralResult("", elapsed, elapsed / audio_seconds if audio_seconds else 0.0,
                                 audio_seconds, config_name,
                                 ok=False, error=f"rc={proc.returncode} | {tail}")

        text = _strip_ansi(proc.stdout).strip()
        rtf = elapsed / audio_seconds if audio_seconds > 0 else float("inf")
        return VoxtralResult(
            text=text,
            elapsed_seconds=elapsed,
            rtf=rtf,
            audio_seconds=audio_seconds,
            config_name=config_name,
            ok=True,
        )


# ── Helpers ───────────────────────────────────────────────────────────

_ANSI_RE = re.compile(r"\x1B\[[0-?]*[ -/]*[@-~]")


def _strip_ansi(s: str) -> str:
    return _ANSI_RE.sub("", s)


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


# Pour compatibilité avec les anciens imports — la version transformers
# exposait DEFAULT_MODEL_ID. Conservé symboliquement.
DEFAULT_MODEL_ID = "mistralai/Voxtral-Mini-3B-2507 (via llama.cpp/Vulkan/GGUF Q4_K_M)"
