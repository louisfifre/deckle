"""Chargement de ``benchmark/.env`` sans dépendance python-dotenv.

Format ``KEY=value`` ligne par ligne, ``#`` pour les commentaires, lignes
vides ignorées. Pas de quotes interprétées (la valeur est prise telle
quelle). C'est volontairement minimaliste — 30 lignes plutôt qu'une
dépendance externe.

Appelée typiquement au début d'un bench :

    from lib.env import load_dotenv
    load_dotenv()        # charge benchmark/.env si présent

Les variables déjà set dans l'environnement ne sont PAS écrasées (le
shell wins). Comme ça si tu exportes ANTHROPIC_API_KEY dans la session,
ça reste prioritaire sur le .env.
"""

from __future__ import annotations

import os
from pathlib import Path


BENCHMARK_DIR = Path(__file__).resolve().parent.parent
DEFAULT_ENV_PATH = BENCHMARK_DIR / ".env"


def load_dotenv(path: Path | None = None, *, override: bool = False) -> dict[str, str]:
    """Charge un fichier .env dans os.environ. Retourne les paires lues.

    Si ``path`` est None, cherche ``benchmark/.env``. Si le fichier
    n'existe pas, retourne dict vide sans erreur (le bench peut s'en
    passer si la clé est déjà exportée dans le shell)."""
    p = path or DEFAULT_ENV_PATH
    if not p.exists():
        return {}

    pairs: dict[str, str] = {}
    for raw_line in p.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        if "=" not in line:
            continue
        key, _, value = line.partition("=")
        key = key.strip()
        value = value.strip()
        # Strip 1 niveau de quotes simples ou doubles (commodité)
        if len(value) >= 2 and value[0] == value[-1] and value[0] in ("'", '"'):
            value = value[1:-1]
        pairs[key] = value
        if override or key not in os.environ:
            os.environ[key] = value
    return pairs
