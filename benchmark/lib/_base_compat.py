"""Helpers transverses à toutes les briques lib/.

Pour l'instant juste un re-encoding stdout UTF-8 pour Windows PowerShell
cp1252. À étendre si on accumule des helpers communs.
"""

from __future__ import annotations

import sys


def _ensure_stdout_utf8() -> None:
    """PowerShell stdout est en cp1252 par défaut sur Windows ; on force
    UTF-8 pour que les box drawing chars, les accents et la sortie des
    modèles ne plantent pas avec ``UnicodeEncodeError``."""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")
        except (AttributeError, OSError):
            pass
