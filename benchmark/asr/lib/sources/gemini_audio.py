"""Gemini as a transcriber, where ``lib/judges/gemini.py`` scores.

It produces the ground-truth reference stored in
``payload.reference_text_gemini``, a sounder WER baseline than the Whisper
reference, which hallucinates. Audio goes inline only; the Files API is not
wired.
"""

from __future__ import annotations

import os
import re
import time
from pathlib import Path
from typing import Any, Callable

from ._base import Source, Transcription


# Flash, not Pro: transcription gains nothing from Pro's reasoning. A pinned
# model rather than ``latest``, so runs stay reproducible.
DEFAULT_MODEL = "gemini-2.5-flash"

# Google caps an inline request at 20 MB; 18 leaves room for the prompt.
DEFAULT_MAX_AUDIO_INLINE_MB = 18.0

# Output is billed by use, so a generous cap costs nothing and never
# truncates a long clip.
DEFAULT_MAX_OUTPUT_TOKENS = 4096

# Same 429 handling as lib/judges/gemini.py.
_RATE_LIMIT_FALLBACK_DELAY_S = 12.0
_RATE_LIMIT_MARGIN_S         = 1.0
_RATE_LIMIT_MAX_RETRIES      = 3


class GeminiAudioSource(Source):
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

    # ── Source interface ───────────────────────────────────────────────

    def transcribe(
        self,
        *,
        audio_path:     Path,
        prompt:         str,
        max_new_tokens: int | None = None,
        on_event:       Callable[[str, dict], None] | None = None,
        system_prompt:  str | None = None,         # ignored: the constructor sets it
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
        """Audio first, then text: the Gemini docs recommend that order so
        the model hears the clip before it reads the instruction."""
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
        """The google-genai SDK does not honour the ``retryDelay`` a 429
        returns, so this sleeps it."""
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
                        # Thinking off: a transcription has nothing to
                        # reason through, so it would only add cost.
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


# ── Helpers ───────────────────────────────────────────────────────────

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
