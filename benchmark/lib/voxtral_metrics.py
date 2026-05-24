"""Objective rule-based metrics for the Voxtral POC bench.

Three families that complement the qualitative judge score :

  - Looping detection (n-gram repetition on sliding windows)
  - Known hallucination strings (prompt leak, training crédit chains)
  - Length sanity (empty or near-empty outputs)

All metrics are local computation only — no LLM call.

Reference : ADR-0011 (POC évaluation Voxtral), section "Méthode de scoring".
"""

from __future__ import annotations

import re
from dataclasses import dataclass


@dataclass(frozen=True)
class ObjectiveMetrics:
    char_count:               int
    word_count:               int
    looping_score:            float     # 0..1, higher = more looping
    longest_repeated_ngram:   tuple[int, int]   # (n, repeat_count)
    hallucination_hits:       list[str]
    prompt_leak_hits:         list[str]


# ── Hallucination & prompt-leak patterns ─────────────────────────────

# Known Whisper hallucinations on French audio (training-data crédits).
_HALLUCINATION_PATTERNS = [
    r"Sous-titrage\s+Soci[ée]t[ée]\s+Radio-Canada",
    r"Sous-titres?\s+r[ée]alis[ée]s?\s+par",
    r"Amara\.org",
    r"Merci\s+(?:d['']avoir\s+regard|de\s+visionner)",
    r"Abonnez-vous\s+(?:à|et\s+activez)",
    r"Like\s+(?:and|et)\s+subscribe",
    r"Thank\s+you\s+for\s+watching",
    r"©\s*Sous-titres?",
]

# Prompt leak — words from the Deckle Whisper initial prompt that
# should never appear in a transcription unless the audio genuinely
# discusses them. The list is intentionally a tight set of high-signal
# tech keywords; broader words like "Mistral" or "Ollama" are not
# flagged because real dictations may legitimately mention them.
_PROMPT_LEAK_PATTERNS = [
    r"\b\.NET\b",
    r"\bVisual\s+Studio\b",
    r"\bWindows\s+App\s+SDK\b",
    r"\bWhisper\b",
    r"\bWinUI\b",
    r"\bC\#\b",
    r"\bP/Invoke\b",
]


def compute(text: str) -> ObjectiveMetrics:
    char_count = len(text)
    words = _tokenize(text)
    word_count = len(words)

    looping_score, longest = _looping(words)
    halluc = _matches(text, _HALLUCINATION_PATTERNS)
    leak   = _matches(text, _PROMPT_LEAK_PATTERNS)

    return ObjectiveMetrics(
        char_count=char_count,
        word_count=word_count,
        looping_score=looping_score,
        longest_repeated_ngram=longest,
        hallucination_hits=halluc,
        prompt_leak_hits=leak,
    )


# ── Internals ─────────────────────────────────────────────────────────

_WORD_RE = re.compile(r"[A-Za-zÀ-ÖØ-öø-ÿ0-9']+", re.UNICODE)


def _tokenize(text: str) -> list[str]:
    return [w.lower() for w in _WORD_RE.findall(text)]


def _looping(words: list[str]) -> tuple[float, tuple[int, int]]:
    """Detect n-gram repetition on sliding windows.

    For n in {3, 4, 5, 6}, count how many distinct n-grams appear at
    least twice consecutively or within a window of 2*n words. The
    score is the fraction of n-grams that loop, max across n. The
    second return value is the (n, max_repeat_count) for the worst
    offender, useful for forensic reporting.
    """
    if len(words) < 6:
        return 0.0, (0, 0)

    worst_score = 0.0
    worst_n = 0
    worst_count = 0

    for n in (3, 4, 5, 6):
        if len(words) < 2 * n:
            continue
        ngrams = [tuple(words[i:i + n]) for i in range(len(words) - n + 1)]
        seen: dict[tuple[str, ...], int] = {}
        for ng in ngrams:
            seen[ng] = seen.get(ng, 0) + 1
        repeats = sum(1 for c in seen.values() if c >= 2)
        local_score = repeats / max(1, len(seen))
        local_max = max(seen.values()) if seen else 0
        if local_score > worst_score:
            worst_score = local_score
            worst_n = n
            worst_count = local_max

    return worst_score, (worst_n, worst_count)


def _matches(text: str, patterns: list[str]) -> list[str]:
    hits: list[str] = []
    for pat in patterns:
        m = re.search(pat, text, re.IGNORECASE | re.UNICODE)
        if m:
            hits.append(m.group(0))
    return hits
