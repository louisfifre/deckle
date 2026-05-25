"""Contrat commun pour tout juge de transcription.

Un juge prend une transcription (et optionnellement une référence) et
produit un score JSON sur N axes définis. Exemples : Claude Haiku par-row
en API Anthropic, Claude Opus en mode macro qui résume tout un run,
Ollama judge local (legacy).

Architecture en deux niveaux :

  - ``score_row(...)``  : juge **un** row individuel (audio, source, régime,
    texte produit). Appelé par le bench dans la boucle. Réponse rapide,
    coût faible — c'est typiquement Haiku.

  - ``score_macro(...)``: juge **un agrégat** de rows. Appelé une fois en
    fin de bench avec un résumé curaté + quelques exemples criants
    sélectionnés. Réponse plus longue, modèle plus gros — c'est typiquement
    Opus, sonnet-thinking, ou GPT-5 si on l'ajoute.

Les deux niveaux retournent un ``JudgeScore`` au schéma libre (dict).
Le bench ne sait pas ce qu'il y a dedans — c'est le rapport final qui
le rend lisible.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any


@dataclass(frozen=True)
class JudgeScore:
    """Score retourné par un juge.

    ``parse_ok = False`` quand la réponse du juge n'était pas du JSON
    valide ; le bench écrit quand même la row avec ``raw_response`` pour
    forensic post-mortem.

    ``axes`` est libre — chaque juge définit ses propres axes selon le
    prompt utilisé. Un rapport final est responsable d'agréger sur les
    axes communs (fidélité, propreté, etc.) s'il veut.
    """
    axes:         dict[str, Any]
    verdict:      str
    raw_response: str
    parse_ok:     bool
    elapsed_s:    float
    model:        str        # ex. "claude-haiku-4-5", "ministral-3:14b"
    extras:       dict[str, Any] = field(default_factory=dict)


class Judge:
    """Interface implicite."""

    name: str       # identifiant court, ex. "claude-haiku", "ollama"
    label: str      # description humaine

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
        """Note un row individuel. Reçoit la transcription hypothesis
        et éventuellement une référence (Whisper large-v3 par défaut).
        ``reference=None`` quand la source EST le juge baseline.

        ``audio_path`` est le WAV source du sample, transmis à titre
        optionnel pour les juges multimodaux capables d'écouter le
        signal (Gemini par exemple). Les juges purement textuels
        (Claude API) reçoivent l'argument mais l'ignorent — la
        signature est uniforme pour que le bench n'ait pas à brancher
        par type de juge.
        """
        raise NotImplementedError

    def score_macro(
        self,
        *,
        run_summary:   dict[str, Any],
        examples:      list[dict[str, Any]],
    ) -> JudgeScore:
        """Note un run complet à partir d'un résumé + exemples curatés.

        ``run_summary`` : dict avec stats agrégées (WER moyen, count par
        source/régime, gen totale, etc.). Le format est défini par le bench
        appelant.

        ``examples`` : liste de rows individuels jugés "intéressants" par
        un autre juge (typiquement Haiku qui sélectionne les rows les
        plus douteux pour qu'Opus les regarde de près).

        Implémentation optionnelle : un juge qui ne sait pas faire de
        macro peut lever NotImplementedError ou retourner un score vide.
        """
        raise NotImplementedError
