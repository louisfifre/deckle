"""Juges de qualité des transcriptions.

Chaque juge implémente le contrat ``_base.Judge.score_row()`` et
optionnellement ``score_macro()``. Le bench instancie un juge et
l'appelle ; l'orchestrateur peut chaîner Haiku (per-row, pas cher) →
Opus (macro, gros raisonnement).
"""

from ._base import Judge, JudgeScore

__all__ = ["Judge", "JudgeScore"]
