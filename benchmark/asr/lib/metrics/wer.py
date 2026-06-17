"""Word Error Rate et Character Error Rate via jiwer.

Wrapper minimal pour éviter que chaque bench importe jiwer directement.
Si on change de lib (jiwer → autre) on touche un seul fichier.

WER : Levenshtein sur tokens mot. CER : Levenshtein sur caractères.
CER est plus stable sur les transcriptions courtes (où un seul mot
faux peut faire grimper le WER à 1.0).
"""

from __future__ import annotations

from typing import NamedTuple

import jiwer


class ErrorRates(NamedTuple):
    wer: float
    cer: float


def compute(reference: str, hypothesis: str) -> ErrorRates:
    """Calcule WER + CER entre une référence et une hypothèse.

    Retourne ``(nan, nan)`` si l'un des deux est vide — sinon jiwer
    lèverait une exception sur la division par zéro.
    """
    ref = reference.strip()
    hyp = hypothesis.strip()
    if not ref or not hyp:
        return ErrorRates(wer=float("nan"), cer=float("nan"))
    try:
        return ErrorRates(
            wer=float(jiwer.wer(ref, hyp)),
            cer=float(jiwer.cer(ref, hyp)),
        )
    except Exception:
        return ErrorRates(wer=float("nan"), cer=float("nan"))
