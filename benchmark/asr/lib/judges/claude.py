"""Claude judge over the Anthropic API, on text only.

``score_row()`` runs once per row on a light model and marks the system
prompt ``cache_control: ephemeral``: every call of a run shares it, so only
the short user part is paid in full. ``score_macro()`` runs once per run on
a large model, over a curated summary and the examples the row judge picked.

The SDK is imported in the constructor, so a bench without Claude never
pays the import.
"""

from __future__ import annotations

import json
import os
import re
import time
from pathlib import Path
from typing import Any

from ._base import Judge, JudgeScore


# Model ids follow Anthropic's catalog:
# https://docs.anthropic.com/en/docs/about-claude/models
DEFAULT_ROW_MODEL   = "claude-haiku-4-5"
DEFAULT_MACRO_MODEL = "claude-opus-4-5"


class ClaudeJudge(Judge):
    name = "claude"
    label = "Claude API (Anthropic)"

    def __init__(
        self,
        *,
        row_system_prompt:   str,
        macro_system_prompt: str | None = None,
        row_model:    str = DEFAULT_ROW_MODEL,
        macro_model:  str = DEFAULT_MACRO_MODEL,
        max_tokens_row:   int = 512,
        max_tokens_macro: int = 4096,
        temperature: float = 0.0,
    ) -> None:
        try:
            import anthropic
        except ImportError as exc:
            raise RuntimeError(
                "Package 'anthropic' requis pour le juge Claude. "
                "Install : pip install anthropic"
            ) from exc

        if not os.environ.get("ANTHROPIC_API_KEY"):
            raise RuntimeError(
                "ANTHROPIC_API_KEY non défini.\n"
                "  Créer un fichier ``benchmark/.env`` avec :\n"
                "    ANTHROPIC_API_KEY=sk-ant-xxxxxxxx\n"
                "  puis appeler lib.env.load_dotenv() avant d'instancier ClaudeJudge,\n"
                "  ou exporter la variable manuellement dans la session shell."
            )

        self._client = anthropic.Anthropic()
        self._anthropic = anthropic
        self.row_system_prompt = row_system_prompt
        self.macro_system_prompt = macro_system_prompt
        self.row_model = row_model
        self.macro_model = macro_model
        self.max_tokens_row = max_tokens_row
        self.max_tokens_macro = max_tokens_macro
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
        # The Anthropic API takes no audio input, so only text is judged;
        # the parameter keeps the shared Judge signature.
        del audio_path
        user_msg = _build_user_message_row(
            hypothesis=hypothesis,
            reference=reference,
            regime_name=regime_name,
            regime_label=regime_label,
            source_name=source_name,
        )
        return self._call(
            system=self.row_system_prompt,
            user=user_msg,
            model=self.row_model,
            max_tokens=self.max_tokens_row,
        )

    # ── Macro ──────────────────────────────────────────────────────────

    def score_macro(
        self,
        *,
        run_summary: dict[str, Any],
        examples:    list[dict[str, Any]],
    ) -> JudgeScore:
        if self.macro_system_prompt is None:
            raise RuntimeError(
                "macro_system_prompt non défini sur ce ClaudeJudge. "
                "Passer macro_system_prompt= au constructeur pour activer "
                "le mode macro."
            )
        user_msg = _build_user_message_macro(
            run_summary=run_summary,
            examples=examples,
        )
        return self._call(
            system=self.macro_system_prompt,
            user=user_msg,
            model=self.macro_model,
            max_tokens=self.max_tokens_macro,
        )

    # ── Internals ──────────────────────────────────────────────────────

    def _call(
        self,
        *,
        system:     str,
        user:       str,
        model:      str,
        max_tokens: int,
    ) -> JudgeScore:
        t0 = time.perf_counter()
        try:
            message = self._client.messages.create(
                model=model,
                max_tokens=max_tokens,
                temperature=self.temperature,
                system=[
                    {
                        "type":          "text",
                        "text":          system,
                        "cache_control": {"type": "ephemeral"},
                    }
                ],
                messages=[
                    {"role": "user", "content": user},
                ],
            )
            response = "".join(
                block.text for block in message.content
                if getattr(block, "type", "") == "text"
            )
        except Exception as exc:
            return JudgeScore(
                axes={}, verdict="", raw_response=f"[ERROR] {exc}",
                parse_ok=False, elapsed_s=time.perf_counter() - t0,
                model=model,
            )

        elapsed = time.perf_counter() - t0
        parsed = _parse_judge_json(response)
        return JudgeScore(
            axes=parsed.get("axes", {}),
            verdict=str(parsed.get("verdict", "")),
            raw_response=response,
            parse_ok=parsed.get("parse_ok", False),
            elapsed_s=elapsed,
            model=model,
            extras={
                "usage": _extract_usage(message) if "message" in dir() else {},
            },
        )


# ── Helpers ──────────────────────────────────────────────────────────

def _build_user_message_row(
    *,
    hypothesis:   str,
    reference:    str | None,
    regime_name:  str,
    regime_label: str,
    source_name:  str,
) -> str:
    """The ``<<<REF>>>`` and ``<<<HYP>>>`` delimiters keep a multi-line
    transcript from blending into the prompt's structure."""
    parts = [
        f"Source       : {source_name}",
        f"Régime       : {regime_name} — {regime_label}",
    ]
    if reference is not None:
        parts.append(
            f"\nRÉFÉRENCE (Whisper large-v3, peut contenir hallucinations) :\n"
            f"<<<REF>>>\n{reference.strip()}\n<<<END>>>"
        )
    parts.append(
        f"\nHYPOTHÈSE à évaluer :\n"
        f"<<<HYP>>>\n{hypothesis.strip()}\n<<<END>>>"
    )
    parts.append(
        "\nRéponds en JSON strict comme spécifié dans le system prompt."
    )
    return "\n".join(parts)


def _build_user_message_macro(
    *,
    run_summary: dict[str, Any],
    examples:    list[dict[str, Any]],
) -> str:
    """Keep it short: the macro judge reasons better over a few chosen
    examples than over a dump of every row."""
    summary_json = json.dumps(run_summary, ensure_ascii=False, indent=2)
    out = [
        "RÉSUMÉ DU RUN (stats agrégées) :",
        "```json",
        summary_json,
        "```",
        "",
        f"EXEMPLES CURATÉS ({len(examples)} rows sélectionnés par le juge per-row) :",
        "",
    ]
    for i, ex in enumerate(examples, 1):
        out.append(f"### Exemple {i}")
        out.append(f"- source : {ex.get('source', '?')}")
        out.append(f"- régime : {ex.get('regime', '?')}")
        out.append(f"- audio  : {ex.get('audio_id', '?')} ({ex.get('duration_s', 0):.1f}s)")
        ref = ex.get("reference")
        if ref:
            out.append(f"- réf    : {ref}")
        out.append(f"- hyp    : {ex.get('hypothesis', '')}")
        verdict = ex.get("row_verdict")
        if verdict:
            out.append(f"- verdict row : {verdict}")
        out.append("")
    out.append("Réponds en JSON strict comme spécifié dans le system prompt.")
    return "\n".join(out)


_JSON_BLOCK_RE = re.compile(r"```(?:json)?\s*(\{.*?\})\s*```", re.DOTALL)
_JSON_OBJ_RE = re.compile(r"\{[\s\S]*\}", re.DOTALL)


def _parse_judge_json(raw: str) -> dict[str, Any]:
    """The model may wrap the object in a ```json fence or surround it
    with prose."""
    m = _JSON_BLOCK_RE.search(raw)
    candidate = m.group(1) if m else None

    if not candidate:
        m2 = _JSON_OBJ_RE.search(raw)
        candidate = m2.group(0) if m2 else None

    if not candidate:
        candidate = raw.strip()

    try:
        obj = json.loads(candidate)
        if not isinstance(obj, dict):
            return {"axes": {}, "verdict": "", "parse_ok": False}
        # Every key that is not a verdict is an axis, so a prompt can add
        # axes without a code change.
        verdict_keys = {"verdict", "verdict_court", "commentaire", "comment"}
        axes = {k: v for k, v in obj.items() if k not in verdict_keys}
        verdict = ""
        for k in ("verdict", "verdict_court", "commentaire", "comment"):
            if k in obj:
                verdict = str(obj[k])
                break
        return {"axes": axes, "verdict": verdict, "parse_ok": True}
    except json.JSONDecodeError:
        return {"axes": {}, "verdict": "", "parse_ok": False}


def _extract_usage(message: Any) -> dict[str, int]:
    try:
        u = message.usage
        return {
            "input_tokens":          int(getattr(u, "input_tokens", 0)),
            "output_tokens":         int(getattr(u, "output_tokens", 0)),
            "cache_read_input_tokens":    int(getattr(u, "cache_read_input_tokens", 0) or 0),
            "cache_creation_input_tokens": int(getattr(u, "cache_creation_input_tokens", 0) or 0),
        }
    except Exception:
        return {}
