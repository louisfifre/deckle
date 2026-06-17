"""Détection de bouclage n-gram dans une transcription.

Whisper a un pathologie connue : il peut entrer en boucle au milieu
d'une transcription et répéter la même phrase 5-10 fois. Encore pire,
la phrase qui boucle peut être copiée au début de l'enregistrement
suivant (contamination d'état entre sessions).

Cette métrique flagge les outputs qui ont des n-grams (n=3..6) qui se
répètent plusieurs fois. Score 0..1 où 1 = beaucoup de bouclage.

À noter : pas de contexte sémantique — un texte qui répète légitimement
"je pense que" plusieurs fois peut être faussement flagué. C'est un
pré-filtre, pas un verdict.
"""

from __future__ import annotations

import re
from typing import NamedTuple


_WORD_RE = re.compile(r"[A-Za-zÀ-ÖØ-öø-ÿ0-9']+", re.UNICODE)


class LoopingMetric(NamedTuple):
    score: float                       # 0..1, plus haut = plus de bouclage
    longest_ngram: tuple[int, int]     # (n, max_repeat_count) pour forensic


def compute(text: str) -> LoopingMetric:
    """Détecte les répétitions n-gram dans ``text``.

    Pour n in {3, 4, 5, 6} : compte les n-grams qui apparaissent ≥ 2 fois.
    Le score est la fraction de n-grams qui se répètent, max sur n.
    """
    words = [w.lower() for w in _WORD_RE.findall(text)]
    if len(words) < 6:
        return LoopingMetric(score=0.0, longest_ngram=(0, 0))

    worst_score = 0.0
    worst_n = 0
    worst_count = 0

    for n in (3, 4, 5, 6):
        if len(words) < 2 * n:
            continue
        ngrams = [tuple(words[i:i + n]) for i in range(len(words) - n + 1)]
        counts: dict[tuple[str, ...], int] = {}
        for ng in ngrams:
            counts[ng] = counts.get(ng, 0) + 1
        repeats = sum(1 for c in counts.values() if c >= 2)
        local_score = repeats / max(1, len(counts))
        local_max = max(counts.values()) if counts else 0
        if local_score > worst_score:
            worst_score = local_score
            worst_n = n
            worst_count = local_max

    return LoopingMetric(score=worst_score, longest_ngram=(worst_n, worst_count))
