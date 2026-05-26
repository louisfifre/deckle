"""Source Gemini multimodale pour la transcription ground-truth.

Cette source utilise ``gemini-2.5-flash`` (ou successeur via param) pour
**produire** une transcription à partir d'un audio. À distinguer du
juge Gemini sous ``lib/judges/gemini.py`` qui **score** une transcription
existante : ici Gemini est traité comme un transcripteur, pas un
évaluateur.

L'usage prévu : générer une référence ground-truth sur le corpus de
validation, comparable à un humain qui écouterait et taperait ce qu'il
entend. Le résultat alimente le champ ``payload.reference_text_gemini``
du corpus.jsonl, puis le bench calcule WER(hypothèse Voxtral vs
référence Gemini) — méthodologiquement plus solide que WER contre la
référence Whisper qui hallucine.

Pourquoi un fichier séparé du juge alors que le SDK est le même : un
fichier = un backend, c'est la convention de ``benchmark/CLAUDE.md``
(§ Concepts). Le juge a une intention différente (scoring contre schéma
fermé) et un prompt système différent (axes 0-100). Mélanger les deux
dans le même fichier serait du couplage gratuit.

Limites :
  - Inline data : max 18 MB (marge sous le hard cap 20 MB Google).
    Couvre confortablement les 330s × 16 kHz mono 16 bit ≈ 10 MB du
    corpus voxtral-val-30. Au-delà, il faudrait câbler Files API.
  - Latence : 3-15 s par sample selon la longueur. Pour 30 samples
    sériels c'est ~5-10 min — acceptable en pré-génération one-shot.
  - Free tier : 5 RPM peut limiter ; le retry sur 429 absorbe les
    bursts (réutilisation du pattern du judge gemini).

Env requis :
  - ``GEMINI_API_KEY`` set, via ``benchmark/.env``. Clé à générer sur
    https://aistudio.google.com/apikey.
"""

from __future__ import annotations

import os
import re
import time
from pathlib import Path
from typing import Any, Callable

from ._base import Source, Transcription


# Modèle par défaut. ``flash`` plutôt que ``pro`` : la transcription
# audio est une tâche déterministe qui ne profite pas du raisonnement
# pro, et flash est ~10× moins cher avec une qualité audio comparable
# sur le français (vérif empirique session perf-cap si on l'a faite,
# sinon à valider en première passe). Snapshot daté plutôt que
# ``latest`` pour reproductibilité bench.
DEFAULT_MODEL = "gemini-2.5-flash"

# Cap inline 18 MB ; au-delà, Files API requise (non câblée ici).
DEFAULT_MAX_AUDIO_INLINE_MB = 18.0

# 4096 : large marge vs ~1320 tokens (330s × 4). Trop bas tronquerait
# les longs ; trop haut ne coûte rien (output billing à l'usage).
DEFAULT_MAX_OUTPUT_TOKENS = 4096

# Pattern de retry 429 — identique à celui du judge gemini.py. Pas
# d'extraction commune en helper transverse pour l'instant : deux
# instances, factorisation prématurée.
_RATE_LIMIT_FALLBACK_DELAY_S = 12.0
_RATE_LIMIT_MARGIN_S         = 1.0
_RATE_LIMIT_MAX_RETRIES      = 3


class GeminiAudioSource(Source):
    """Gemini comme transcripteur ground-truth."""

    name = "gemini-audio"
    label = "Gemini API (audio multimodal)"

    def __init__(
        self,
        *,
        system_prompt:       str,
        model:               str   = DEFAULT_MODEL,
        max_audio_inline_mb: float = DEFAULT_MAX_AUDIO_INLINE_MB,
        max_output_tokens:   int   = DEFAULT_MAX_OUTPUT_TOKENS,
        temperature:         float = 0.0,
    ) -> None:
        try:
            from google import genai
            from google.genai import types
        except ImportError as exc:
            raise RuntimeError(
                "Package 'google-genai' requis pour la source Gemini audio. "
                "Install dans le venv : pip install google-genai"
            ) from exc

        if not os.environ.get("GEMINI_API_KEY"):
            raise RuntimeError(
                "GEMINI_API_KEY non défini.\n"
                "  Créer un fichier benchmark/.env avec :\n"
                "    GEMINI_API_KEY=AIza...\n"
                "  Clé à générer sur https://aistudio.google.com/apikey\n"
                "  puis appeler lib.env.load_dotenv() avant d'instancier "
                "GeminiAudioSource."
            )

        self._client = genai.Client(api_key=os.environ["GEMINI_API_KEY"])
        self._types  = types
        self.system_prompt       = system_prompt
        self.model               = model
        self.max_audio_inline_mb = max_audio_inline_mb
        self.max_output_tokens   = max_output_tokens
        self.temperature         = temperature

    # ── Interface Source ───────────────────────────────────────────────

    def transcribe(
        self,
        *,
        audio_path:     Path,
        prompt:         str,
        max_new_tokens: int | None = None,
        on_event:       Callable[[str, dict], None] | None = None,
        system_prompt:  str | None = None,         # ignoré : on l'a au ctor
    ) -> Transcription:
        audio_s = _wav_duration_seconds(audio_path)
        t0 = time.perf_counter()

        try:
            parts = self._build_parts(audio_path=audio_path, prompt=prompt)
        except Exception as exc:
            return Transcription(
                text="", elapsed_s=time.perf_counter() - t0,
                audio_s=audio_s, rtf=0.0, generated_tokens=-1,
                ok=False, error=f"{type(exc).__name__}: {exc}",
            )

        max_tokens = max_new_tokens or self.max_output_tokens
        try:
            response = self._call_with_retry(parts, max_tokens)
            text = (response.text or "").strip()
        except Exception as exc:
            return Transcription(
                text="", elapsed_s=time.perf_counter() - t0,
                audio_s=audio_s, rtf=0.0, generated_tokens=-1,
                ok=False, error=f"{type(exc).__name__}: {exc}",
            )

        elapsed = time.perf_counter() - t0
        usage   = _extract_usage(response)
        return Transcription(
            text=text,
            elapsed_s=elapsed,
            audio_s=audio_s,
            rtf=elapsed / audio_s if audio_s > 0 else float("inf"),
            generated_tokens=usage.get("output_tokens", -1) or -1,
            ok=True,
            extras={
                "model":            self.model,
                "temperature":      self.temperature,
                "user_prompt_chars": len(prompt),
                "system_prompt_chars": len(self.system_prompt),
                "usage":            usage,
            },
        )

    # ── Internals ──────────────────────────────────────────────────────

    def _build_parts(self, *, audio_path: Path, prompt: str) -> list[Any]:
        """Construit la liste ``contents``. Ordre : audio d'abord, texte
        ensuite — recommandé par la doc Gemini pour que le modèle écoute
        avant de lire l'instruction."""
        if not audio_path.exists():
            raise FileNotFoundError(f"audio_path inexistant : {audio_path}")
        size_mb = audio_path.stat().st_size / (1024.0 * 1024.0)
        if size_mb > self.max_audio_inline_mb:
            raise RuntimeError(
                f"Audio {audio_path.name} pèse {size_mb:.1f} MB > "
                f"{self.max_audio_inline_mb} MB. Files API non câblée."
            )
        data = audio_path.read_bytes()
        mime = _mime_from_ext(audio_path.suffix)
        return [
            self._types.Part.from_bytes(data=data, mime_type=mime),
            prompt,
        ]

    def _call_with_retry(self, parts: list[Any], max_tokens: int) -> Any:
        """Wrapper retry 429. Pattern identique à
        ``lib.judges.gemini._call_with_retry`` : le SDK google-genai
        ne respecte pas tout seul le ``retryDelay`` du payload d'erreur."""
        last_exc: Exception | None = None
        for attempt in range(_RATE_LIMIT_MAX_RETRIES):
            try:
                return self._client.models.generate_content(
                    model=self.model,
                    contents=parts,
                    config=self._types.GenerateContentConfig(
                        system_instruction=self.system_prompt,
                        temperature=self.temperature,
                        max_output_tokens=max_tokens,
                        # Thinking désactivé : la transcription est une
                        # tâche directe, le chain-of-thought ajoute coût
                        # et latence sans bénéfice qualitatif observable.
                        thinking_config=self._types.ThinkingConfig(thinking_budget=0),
                    ),
                )
            except Exception as exc:
                last_exc = exc
                if not _is_rate_limit_error(exc):
                    raise
                if attempt == _RATE_LIMIT_MAX_RETRIES - 1:
                    raise
                delay = _extract_retry_delay(exc) or _RATE_LIMIT_FALLBACK_DELAY_S
                time.sleep(delay + _RATE_LIMIT_MARGIN_S)
        raise last_exc if last_exc is not None else RuntimeError("retry loop exited")


# ── Helpers libres ────────────────────────────────────────────────────

def _is_rate_limit_error(exc: Exception) -> bool:
    code = getattr(exc, "code", None) or getattr(exc, "status_code", None)
    if code == 429:
        return True
    s = str(exc)
    return "429" in s and ("RESOURCE_EXHAUSTED" in s or "rate limit" in s.lower())


_RETRY_DELAY_PATTERNS = (
    re.compile(r"'retryDelay':\s*'(\d+(?:\.\d+)?)s'"),
    re.compile(r'"retryDelay":\s*"(\d+(?:\.\d+)?)s"'),
    re.compile(r"retry in (\d+(?:\.\d+)?)\s*s"),
)


def _extract_retry_delay(exc: Exception) -> float | None:
    details = getattr(exc, "details", None)
    if isinstance(details, list):
        for d in details:
            if isinstance(d, dict) and d.get("@type", "").endswith("RetryInfo"):
                rd = str(d.get("retryDelay", ""))
                m = re.match(r"^(\d+(?:\.\d+)?)s$", rd)
                if m:
                    return float(m.group(1))
    s = str(exc)
    for pat in _RETRY_DELAY_PATTERNS:
        m = pat.search(s)
        if m:
            return float(m.group(1))
    return None


def _mime_from_ext(ext: str) -> str:
    return {
        ".wav":  "audio/wav",
        ".mp3":  "audio/mpeg",
        ".m4a":  "audio/mp4",
        ".ogg":  "audio/ogg",
        ".flac": "audio/flac",
    }.get(ext.lower(), "audio/wav")


def _extract_usage(response: Any) -> dict[str, int]:
    try:
        meta = response.usage_metadata
        return {
            "input_tokens":  int(getattr(meta, "prompt_token_count", 0) or 0),
            "output_tokens": int(getattr(meta, "candidates_token_count", 0) or 0),
            "total_tokens":  int(getattr(meta, "total_token_count", 0) or 0),
        }
    except Exception:
        return {}


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
