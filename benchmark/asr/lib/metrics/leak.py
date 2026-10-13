"""Detect known ASR hallucinations and, on request, prompt leaks."""

from __future__ import annotations

import re
from dataclasses import dataclass


# Subtitle and credit lines from Whisper's training data, which surface on
# near-silent or noisy French audio.
_HALLUCINATION_PATTERNS: tuple[str, ...] = (
    r"Sous-titrage\s+Soci[ée]t[ée]\s+Radio-Canada",
    r"Sous-titres?\s+r[ée]alis[ée]s?\s+par",
    r"Amara\.org",
    r"Merci\s+(?:d['']avoir\s+regard|de\s+visionner)",
    r"Abonnez-vous\s+(?:à|et\s+activez)",
    r"Like\s+(?:and|et)\s+subscribe",
    r"Thank\s+you\s+for\s+watching",
    r"©\s*Sous-titres?",
)


@dataclass(frozen=True)
class LeakReport:
    hallucinations: list[str]
    custom_leaks:   list[str]


def detect(text: str, *, custom_patterns: tuple[str, ...] = ()) -> LeakReport:
    """``custom_patterns`` takes the words of a prompt to flag as leaks. It
    is empty by default: on a tech corpus the audio legitimately says those
    words, so leak patterns give false positives.
    """
    return LeakReport(
        hallucinations=_match_all(text, _HALLUCINATION_PATTERNS),
        custom_leaks=_match_all(text, custom_patterns) if custom_patterns else [],
    )


def _match_all(text: str, patterns: tuple[str, ...]) -> list[str]:
    hits: list[str] = []
    for pat in patterns:
        m = re.search(pat, text, re.IGNORECASE | re.UNICODE)
        if m:
            hits.append(m.group(0))
    return hits
