"""Contrat commun pour toute source de transcription.

Une "source" est un backend qui prend un audio et un prompt court, et
produit du texte transcrit. Exemples : whisper.cpp via whisper-cli,
Voxtral via Transformers+DirectML, futur Voxtral via vLLM Linux, etc.

Le contrat est minimal : une méthode ``transcribe()`` qui prend un audio
et renvoie un ``Transcription``. Le coût de chargement du modèle est
porté par le constructeur ; transcribe() doit être appelable plusieurs
fois sans re-charger (un bench complet = N samples × M régimes ; on
n'a pas envie de recharger le modèle à chaque appel).

Pourquoi pas une classe abstraite formelle : Python duck-typing suffit.
Les sources implémentent simplement la méthode. La signature est figée
ici pour qu'un bench puisse swap whisper → voxtral sans rien changer.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable


@dataclass(frozen=True)
class Transcription:
    """Résultat d'une transcription par une Source.

    Tous les champs sont obligatoires sauf ``extras`` (par-source).
    ``rtf = elapsed_s / audio_s`` ; < 1.0 = plus rapide que temps réel.
    """
    text:            str
    elapsed_s:       float
    audio_s:         float
    rtf:             float
    generated_tokens: int          # -1 si la source ne le rapporte pas
    ok:              bool
    error:           str = ""
    # Données spécifiques à la source (prep_s, dtype, n_params, etc.).
    # Sérialisable JSON.
    extras:          dict[str, Any] = field(default_factory=dict)


class Source:
    """Interface implicite. Les implémentations héritent OU duck-type."""

    name: str          # identifiant court, ex. "voxtral-dml", "whisper-cpp"
    label: str         # description humaine pour les rapports

    def transcribe(
        self,
        *,
        audio_path:     Path,
        prompt:         str,
        max_new_tokens: int | None = None,
        on_event:       Callable[[str, dict], None] | None = None,
    ) -> Transcription:
        """Transcrit un audio.

        ``max_new_tokens=None`` → la source décide (typiquement adaptatif
        selon la durée audio pour éviter la troncature sur les longs).

        ``on_event`` est un callback optionnel qu'utilise la source pour
        signaler des événements anormaux (``row_oom_caught``,
        ``row_retry_start``, etc.). Le bench branche son ``EventLog`` ici.
        """
        raise NotImplementedError

    def warmup(self) -> None:
        """Optionnel : exécute une transcription factice pour amorcer
        caches GPU. Par défaut no-op."""
        return None
