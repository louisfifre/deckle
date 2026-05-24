"""Drivers de transcription.

Chaque fichier ``<name>.py`` expose une classe Source qui implémente
le contrat de ``_base.Source.transcribe()``. Le bench peut swap d'une
source à l'autre sans rien changer côté orchestrateur.
"""

from ._base import Source, Transcription

__all__ = ["Source", "Transcription"]
