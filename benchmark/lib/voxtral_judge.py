"""Ollama judge wrapper for the Voxtral POC bench.

Sends each transcription output to a local Mistral-family model
(default ``ministral-3:14b``) with the dedicated judge system prompt
and parses the strict JSON response. No remote API call ever.

Reference : ADR-0011 (POC évaluation Voxtral), section "Méthode de scoring".
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import ollama


DEFAULT_JUDGE_MODEL = "ministral-3:14b"


@dataclass(frozen=True)
class JudgeScore:
    fidelite:        int
    proprete:        int
    absence_leak:    int
    regime_respecte: int
    verdict_court:   str
    raw_response:    str          # for forensic if parse partial
    parse_ok:        bool

    @property
    def aggregate(self) -> float:
        """Mean of the 4 axes (0-100). Convenience for sort/filter."""
        return (self.fidelite + self.proprete
                + self.absence_leak + self.regime_respecte) / 4.0


def score(
    *,
    transcription:   str,
    config_name:     str,
    config_label:    str,
    judge_system:    str,
    model:           str = DEFAULT_JUDGE_MODEL,
    endpoint:        str = "http://localhost:11434",
) -> JudgeScore:
    """Send one transcription to the judge model and parse its verdict.

    The judge receives the system prompt (judge_system) and a user
    message that includes the régime context and the transcription
    text. The judge is asked to return strict JSON; we extract the
    first JSON object found in its response.
    """
    user_message = _build_user_message(
        config_name=config_name,
        config_label=config_label,
        transcription=transcription,
    )

    client = ollama.Client(host=endpoint)
    response = client.chat(
        model=model,
        messages=[
            {"role": "system", "content": judge_system},
            {"role": "user",   "content": user_message},
        ],
        options={
            "temperature": 0.1,    # near-deterministic
            "num_ctx":     16384,  # plenty for transcription + prompt
        },
    )
    raw = response["message"]["content"].strip()
    return _parse(raw)


def _build_user_message(
    *,
    config_name:   str,
    config_label:  str,
    transcription: str,
) -> str:
    return (
        f"Régime : {config_name} — {config_label}\n"
        f"\n"
        f"Transcription à évaluer :\n"
        f"\"\"\"\n{transcription}\n\"\"\"\n"
        f"\n"
        f"Réponds en JSON strict selon le format spécifié."
    )


_JSON_OBJECT_RE = re.compile(r"\{[^{}]*\}", re.DOTALL)


def _parse(raw: str) -> JudgeScore:
    """Extract the first JSON object from the raw response and validate
    the four required integer fields. On parse error, return a
    JudgeScore with parse_ok=False and zeros so the bench can still
    record the row."""
    # Try direct parse first (strict-JSON-only response).
    try:
        obj = json.loads(raw)
    except json.JSONDecodeError:
        # Fallback : scan for the first {...} block.
        match = _JSON_OBJECT_RE.search(raw)
        if not match:
            return JudgeScore(0, 0, 0, 0, "", raw, parse_ok=False)
        try:
            obj = json.loads(match.group(0))
        except json.JSONDecodeError:
            return JudgeScore(0, 0, 0, 0, "", raw, parse_ok=False)

    if not isinstance(obj, dict):
        return JudgeScore(0, 0, 0, 0, "", raw, parse_ok=False)

    def _i(key: str) -> int:
        v = obj.get(key, 0)
        try:
            return max(0, min(100, int(v)))
        except (TypeError, ValueError):
            return 0

    return JudgeScore(
        fidelite=_i("fidelite"),
        proprete=_i("proprete"),
        absence_leak=_i("absence_leak"),
        regime_respecte=_i("regime_respecte"),
        verdict_court=str(obj.get("verdict_court", "")).strip(),
        raw_response=raw,
        parse_ok=True,
    )


def load_judge_system_prompt(path: Path) -> str:
    return path.read_text(encoding="utf-8").strip()
