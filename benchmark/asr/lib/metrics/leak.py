"""Détection de patterns d'hallucination et de prompt leak.

Deux familles de patterns :

  - **Hallucinations connues** : chaînes que Whisper produit sur audio
    quasi-vide ou bruit, héritage de son training-data (sous-titres
    YouTube, crédits TV). C'est rare en français mais bien documenté.

  - **Prompt leak suspects** : tokens qui apparaîtraient si le modèle
    "fuyait" son initial prompt dans la sortie. **Désactivé par défaut
    en v2** parce que ça génère trop de faux positifs sur du corpus tech
    (l'audio mentionne légitimement ces mots). À ré-activer cas par cas
    avec ``patterns=`` paramétrable.

Les patterns hallucination restent compilés en dur — c'est un set
fermé de chaînes que ni Voxtral ni Whisper ne devraient générer
spontanément.
"""

from __future__ import annotations

import re
from dataclasses import dataclass


# Hallucinations Whisper connues sur audio français bruit/silence.
# Trainings-data crédits qui remontent quand le signal est pauvre.
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
    hallucinations: list[str]    # matches sur _HALLUCINATION_PATTERNS
    custom_leaks:   list[str]    # matches sur patterns custom optionnels


def detect(text: str, *, custom_patterns: tuple[str, ...] = ()) -> LeakReport:
    """Scanne ``text`` pour des hallucinations + patterns custom.

    ``custom_patterns`` est laissé vide par défaut. Si tu veux flagger
    des mots-clés d'un prompt système (ex. pour le bench Deckle Whisper
    qui a un initial_prompt avec .NET / WinUI / Whisper), passe-les ici.
    Mais réfléchis bien : sur du corpus tech, ces mots peuvent
    apparaître légitimement dans l'audio source.
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
