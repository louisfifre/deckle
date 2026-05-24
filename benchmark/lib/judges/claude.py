"""Juge Claude via l'API Anthropic.

Architecture en deux niveaux (cf. ``_base.Judge`` docstring) :

  - ``score_row()``   : juge un row, modèle léger (Haiku par défaut).
    Cache le system prompt via ``cache_control: ephemeral`` — un run
    de bench fait N appels avec le MÊME system, donc les tokens d'input
    sont mis en cache après le 1er appel et le coût total est dominé
    par les ~quelques centaines de tokens user variables.

  - ``score_macro()`` : juge un agrégat de rows, modèle gros (Opus
    par défaut). Reçoit un résumé curaté + exemples sélectionnés par
    le juge per-row. Une seule fois en fin de run.

Le SDK est importé en lazy à l'instanciation pour qu'un bench qui n'use
pas Claude (ex. juge Ollama only) ne paie pas le coût d'import.

Env requis :
  - ``ANTHROPIC_API_KEY`` doit être set. Le SDK le lit automatiquement.
    Loadé via ``lib.env.load_dotenv()`` à l'entrée du bench.
"""

from __future__ import annotations

import json
import os
import re
import time
from typing import Any

from ._base import Judge, JudgeScore


# Modèles par défaut — paramétrables au constructeur. Les noms exacts
# dépendent du catalog Anthropic au moment du run ; voir
# https://docs.anthropic.com/en/docs/about-claude/models pour la liste à jour.
DEFAULT_ROW_MODEL   = "claude-haiku-4-5"
DEFAULT_MACRO_MODEL = "claude-opus-4-5"


class ClaudeJudge(Judge):
    """Juge Claude (per-row + macro)."""

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
    ) -> JudgeScore:
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
    """Construit le message user pour un appel score_row.

    Format choisi pour rester lisible si on inspecte le raw côté juge
    (Anthropic console, replay manuel). Délimiteurs ``<<<TEXT>>>`` pour
    que les transcriptions multi-lignes ne se mélangent pas avec la
    structure du prompt.
    """
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
    """Construit le message user pour score_macro.

    Format JSON pour le résumé (lisible et facile à parser côté juge),
    Markdown pour les exemples (lisible humain). Le total reste court —
    le but est qu'Opus puisse réfléchir longtemps sur peu de matière
    bien sélectionnée, pas qu'il moule dans un dump de 50 rows."""
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
    """Parse la réponse JSON du juge. Robust à un éventuel wrapping en
    ```json ... ``` ou à du texte avant/après l'objet. Retourne :

      { "axes": {...}, "verdict": str, "parse_ok": bool }
    """
    # 1. Bloc markdown ```json ... ```
    m = _JSON_BLOCK_RE.search(raw)
    candidate = m.group(1) if m else None

    # 2. À défaut, premier {...} dans le texte
    if not candidate:
        m2 = _JSON_OBJ_RE.search(raw)
        candidate = m2.group(0) if m2 else None

    # 3. Direct parse en dernier
    if not candidate:
        candidate = raw.strip()

    try:
        obj = json.loads(candidate)
        if not isinstance(obj, dict):
            return {"axes": {}, "verdict": "", "parse_ok": False}
        # Convention : tous les champs sauf 'verdict' / 'verdict_court' /
        # 'commentaire' sont des axes. Plus tolérant qu'un schéma fermé.
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
    """Extrait le compte de tokens d'une réponse Anthropic. Optionnel."""
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
