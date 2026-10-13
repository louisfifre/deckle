"""Gemini judge over the ``google-genai`` SDK, not the deprecated
``google-generativeai``.

The multimodal judge: Gemini hears the WAV, so ``fidelite_signal`` is
measured against the audio rather than inferred by comparing texts, which
the text-only Claude judge cannot do. ``response_schema`` forces strict
JSON. The SDK is imported in the constructor, so a bench without Gemini
never pays the import. ``score_macro`` is not implemented.
"""

from __future__ import annotations

import json
import os
import re
import time
from pathlib import Path
from typing import Any

from ._base import Judge, JudgeScore


# Used only when a 429 carries no parsable ``retryDelay``. 12 s spaces calls
# at 5 per minute, a free-tier quota, so it errs safe on a paid tier.
_RATE_LIMIT_FALLBACK_DELAY_S = 12.0
_RATE_LIMIT_MARGIN_S = 1.0
_RATE_LIMIT_MAX_RETRIES = 3


# A pinned model rather than ``gemini-flash-latest``, so runs stay reproducible.
DEFAULT_ROW_MODEL = "gemini-3.5-flash"


# Same axes as the Claude judge so the two compare, except that
# ``fidelite_signal`` here is measured against the audio.
_JUDGE_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "fidelite_signal":       {"type": "integer", "minimum": 0, "maximum": 100},
        "proprete":              {"type": "integer", "minimum": 0, "maximum": 100},
        "absence_hallucination": {"type": "integer", "minimum": 0, "maximum": 100},
        "regime_respecte":       {"type": "integer", "minimum": 0, "maximum": 100},
        "whisper_ref_suspecte":  {"type": "boolean"},
        "verdict":               {"type": "string"},
    },
    "required": [
        "fidelite_signal",
        "proprete",
        "absence_hallucination",
        "regime_respecte",
        "whisper_ref_suspecte",
        "verdict",
    ],
    "propertyOrdering": [
        "fidelite_signal",
        "proprete",
        "absence_hallucination",
        "regime_respecte",
        "whisper_ref_suspecte",
        "verdict",
    ],
}


class GeminiJudge(Judge):
    name = "gemini"
    label = "Gemini API (Google AI Studio)"

    def __init__(
        self,
        *,
        row_system_prompt:   str,
        row_model:           str = DEFAULT_ROW_MODEL,
        max_audio_inline_mb: float = 18.0,
        max_tokens_row:      int = 512,
        temperature:         float = 0.0,
    ) -> None:
        try:
            from google import genai
            from google.genai import types
        except ImportError as exc:
            raise RuntimeError(
                "Package 'google-genai' requis pour le juge Gemini. "
                "Install dans le venv : pip install google-genai"
            ) from exc

        if not os.environ.get("GEMINI_API_KEY"):
            raise RuntimeError(
                "GEMINI_API_KEY non défini.\n"
                "  Créer un fichier benchmark/.env avec :\n"
                "    GEMINI_API_KEY=AIza...\n"
                "  Clé à générer sur https://aistudio.google.com/apikey\n"
                "  puis appeler lib.env.load_dotenv() avant d'instancier GeminiJudge."
            )

        self._client = genai.Client(api_key=os.environ["GEMINI_API_KEY"])
        self._types = types
        self.row_system_prompt = row_system_prompt
        self.row_model = row_model
        # Google caps an inline request at 20 MB; 18 leaves room for the
        # text sent with the audio. Larger audio raises, since the Files
        # API path is not wired.
        self.max_audio_inline_mb = max_audio_inline_mb
        # Thinking is off (see _call_with_retry), so 512 covers the
        # schema's JSON and a long verdict, with little room for runaway
        # output.
        self.max_tokens_row = max_tokens_row
        self.temperature = temperature

    # ── Per-row ────────────────────────────────────────────────────────

    def score_row(
        self,
        *,
        hypothesis:    str,
        reference:     str | None,
        regime_name:   str,
        regime_label:  str,
        source_name:   str,
        audio_path:    Path | None = None,
    ) -> JudgeScore:
        t0 = time.perf_counter()
        try:
            parts = self._build_parts(
                hypothesis=hypothesis,
                reference=reference,
                regime_name=regime_name,
                regime_label=regime_label,
                source_name=source_name,
                audio_path=audio_path,
            )
            response = self._call_with_retry(parts)
            raw = response.text or ""
        except Exception as exc:
            return JudgeScore(
                axes={}, verdict="", raw_response=f"[ERROR] {type(exc).__name__}: {exc}",
                parse_ok=False, elapsed_s=time.perf_counter() - t0,
                model=self.row_model,
            )

        elapsed = time.perf_counter() - t0
        parsed = _parse_json(raw)
        return JudgeScore(
            axes=parsed["axes"],
            verdict=parsed["verdict"],
            raw_response=raw,
            parse_ok=parsed["parse_ok"],
            elapsed_s=elapsed,
            model=self.row_model,
            extras={"usage": _extract_usage(response)},
        )

    # ── Macro (not wired) ──────────────────────────────────────────────

    def score_macro(self, *, run_summary, examples):
        raise NotImplementedError(
            "GeminiJudge.score_macro pas câblé. À implémenter quand "
            "Gemini 3.5 Pro sera disponible, ou utiliser un juge gros "
            "alternatif pour le macro."
        )

    # ── Internals ──────────────────────────────────────────────────────

    def _call_with_retry(self, parts: list[Any]) -> Any:
        """The ``google-genai`` SDK does not honour the ``retryDelay`` a 429
        returns, so this sleeps it plus a margin. Past three attempts the
        cause is likely the plan or the key, so the error goes up.
        """
        last_exc: Exception | None = None
        for attempt in range(_RATE_LIMIT_MAX_RETRIES):
            try:
                return self._client.models.generate_content(
                    model=self.row_model,
                    contents=parts,
                    config=self._types.GenerateContentConfig(
                        system_instruction=self.row_system_prompt,
                        response_mime_type="application/json",
                        response_schema=_JUDGE_SCHEMA,
                        temperature=self.temperature,
                        max_output_tokens=self.max_tokens_row,
                        # Thinking off: the schema leaves nothing to reason
                        # through, and calls run much faster. It lifts
                        # fidelite_signal and proprete uniformly, so rankings
                        # between regimes and sources hold, but scores do
                        # not compare with a thinking-on run.
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
        # Unreachable; type checkers want an explicit exit.
        raise last_exc if last_exc is not None else RuntimeError("retry loop exited unexpectedly")

    def _build_parts(
        self,
        *,
        hypothesis:   str,
        reference:    str | None,
        regime_name:  str,
        regime_label: str,
        source_name:  str,
        audio_path:   Path | None,
    ) -> list[Any]:
        """Audio first, then text: the Gemini docs recommend that order so
        the model hears the clip before it reads what to judge."""
        parts: list[Any] = []

        if audio_path is not None:
            if not audio_path.exists():
                raise FileNotFoundError(f"audio_path inexistant : {audio_path}")
            size_mb = audio_path.stat().st_size / (1024.0 * 1024.0)
            if size_mb > self.max_audio_inline_mb:
                raise RuntimeError(
                    f"Audio {audio_path.name} pèse {size_mb:.1f} MB > "
                    f"{self.max_audio_inline_mb} MB. Files API non câblée "
                    f"pour ce juge — sample trop long pour inline_data."
                )
            data = audio_path.read_bytes()
            mime = _mime_from_ext(audio_path.suffix)
            parts.append(self._types.Part.from_bytes(data=data, mime_type=mime))

        text_msg = _build_text_message_row(
            hypothesis=hypothesis,
            reference=reference,
            regime_name=regime_name,
            regime_label=regime_label,
            source_name=source_name,
            has_audio=audio_path is not None,
        )
        parts.append(text_msg)
        return parts


# ── Helpers ───────────────────────────────────────────────────────────

def _is_rate_limit_error(exc: Exception) -> bool:
    """The SDK exposes the HTTP code under different attributes across
    versions, hence the fallback on the message text. A false positive
    only costs a sleep."""
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


def _build_text_message_row(
    *,
    hypothesis:   str,
    reference:    str | None,
    regime_name:  str,
    regime_label: str,
    source_name:  str,
    has_audio:    bool,
) -> str:
    """No JSON reminder here, unlike the Claude judge: ``response_schema``
    already enforces it."""
    parts = [
        f"Source       : {source_name}",
        f"Régime       : {regime_name} — {regime_label}",
    ]
    if has_audio:
        parts.append("Audio        : fourni en entrée (écoute le signal).")
    if reference is not None:
        parts.append(
            f"\nRÉFÉRENCE (Whisper large-v3, peut contenir hallucinations) :\n"
            f"<<<REF>>>\n{reference.strip()}\n<<<END>>>"
        )
    parts.append(
        f"\nHYPOTHÈSE à évaluer :\n"
        f"<<<HYP>>>\n{hypothesis.strip()}\n<<<END>>>"
    )
    return "\n".join(parts)


def _parse_json(raw: str) -> dict[str, Any]:
    """``response_schema`` makes ``raw`` plain JSON, so no fence recovery
    as in ``claude.py``. Every key that is not a verdict is an axis, as
    there."""
    if not raw or not raw.strip():
        return {"axes": {}, "verdict": "", "parse_ok": False}
    try:
        obj = json.loads(raw)
    except json.JSONDecodeError:
        return {"axes": {}, "verdict": "", "parse_ok": False}
    if not isinstance(obj, dict):
        return {"axes": {}, "verdict": "", "parse_ok": False}
    verdict_keys = {"verdict", "verdict_court", "commentaire", "comment"}
    axes = {k: v for k, v in obj.items() if k not in verdict_keys}
    verdict = ""
    for k in ("verdict", "verdict_court", "commentaire", "comment"):
        if k in obj:
            verdict = str(obj[k])
            break
    return {"axes": axes, "verdict": verdict, "parse_ok": True}


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
