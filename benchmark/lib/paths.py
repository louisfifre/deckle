"""Paths du bench — séparation entre code (worktree) et data (AppData).

Le **code** (lib/, benches/, viewers/, scripts/) vit dans le repo et bouge
avec le worktree courant. Les **données** (corpora curated, runs avec
ground truth Gemini, runs avec verdicts judge) sont précieuses et doivent
**survivre aux worktrees** — sinon chaque rebase ou nettoyage les perd.

La séparation :

  - ``BENCHMARK_CODE_DIR`` — résolu depuis ``__file__`` du module, pointe
    sur le dossier ``benchmark/`` du worktree courant.
  - ``BENCHMARK_DATA_DIR`` — par défaut ``%LOCALAPPDATA%\\Deckle\\benchmark\\``,
    survit aux worktrees. Override via ``DECKLE_BENCHMARK_DIR``.

Tout consommateur qui lit/écrit des **résultats** ou des **corpora** passe
par ``CORPORA_DIR`` et ``RUNS_DIR`` exposés ici.

Nommage canonique d'un run : ``<modèle>-<phase>-<NNNN>`` où :
  - ``modèle`` est le slug du modèle testé (``voxtral``, ``whisper``,
    ``gemma3``)
  - ``phase`` ∈ ``{poc, debug, testing, integration}`` — le bench est un
    harnais récurrent, pas un one-shot
  - ``NNNN`` est un compteur incrémental à 4 chiffres par couple
    (modèle, phase)

Exemples : ``voxtral-poc-0001``, ``voxtral-debug-0003``,
``whisper-testing-0001``.

Tri naturel : modèle d'abord, phase ensuite, id en dernier.
"""

from __future__ import annotations

import os
import re
from pathlib import Path

# ── Code (worktree) ─────────────────────────────────────────────────

BENCHMARK_CODE_DIR = Path(__file__).resolve().parent.parent
"""Le ``benchmark/`` du worktree courant. Ne contient que du code et des
templates de prompts versionnés. Ne contient pas de résultats."""


# ── Data (AppData, survit aux worktrees) ────────────────────────────

def _resolve_data_dir() -> Path:
    override = os.environ.get("DECKLE_BENCHMARK_DIR")
    if override:
        return Path(override)
    localappdata = os.environ.get("LOCALAPPDATA")
    if not localappdata:
        # Fallback minimal — ne devrait pas arriver sous Windows.
        return BENCHMARK_CODE_DIR
    return Path(localappdata) / "Deckle" / "benchmark"


BENCHMARK_DATA_DIR = _resolve_data_dir()
"""Racine des données persistantes du bench. Survit aux worktrees."""

CORPORA_DIR = BENCHMARK_DATA_DIR / "corpora"
"""Corpora curated avec leur ``corpus.jsonl`` enrichi (ground truth
Gemini, références Whisper sticky, etc.). Un sous-dossier par slug."""

RUNS_DIR = BENCHMARK_DATA_DIR / "runs"
"""Résultats des passes de bench. Un sous-dossier par run au format
``<modèle>-<phase>-<NNNN>``."""


# ── Helpers ─────────────────────────────────────────────────────────

_RUN_NAME_RE = re.compile(r"^(?P<model>[a-z0-9]+)-(?P<phase>[a-z]+)-(?P<id>\d{4})$")


def next_run_id(model: str, phase: str) -> int:
    """Renvoie le prochain id à 4 chiffres pour un couple (modèle, phase),
    en scannant ``RUNS_DIR`` pour les runs existants. Démarre à 1."""
    if not RUNS_DIR.exists():
        return 1
    prefix = f"{model}-{phase}-"
    used = []
    for d in RUNS_DIR.iterdir():
        if not d.is_dir():
            continue
        if not d.name.startswith(prefix):
            continue
        m = _RUN_NAME_RE.match(d.name)
        if m and m["model"] == model and m["phase"] == phase:
            used.append(int(m["id"]))
    return (max(used) + 1) if used else 1


def make_run_dir(model: str, phase: str, *, create: bool = True) -> Path:
    """Construit un nouveau ``RUNS_DIR/<modèle>-<phase>-<NNNN>``. Le NNNN
    est calculé automatiquement comme ``next_run_id(model, phase)``.

    Si ``create=True`` (défaut), le dossier est créé et ses parents."""
    n = next_run_id(model, phase)
    run_dir = RUNS_DIR / f"{model}-{phase}-{n:04d}"
    if create:
        run_dir.mkdir(parents=True, exist_ok=True)
    return run_dir


def corpus_dir(slug: str) -> Path:
    """Renvoie ``CORPORA_DIR / slug``. Ne crée pas le dossier — utilisé
    en lecture comme en écriture."""
    return CORPORA_DIR / slug
