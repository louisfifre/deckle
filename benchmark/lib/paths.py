"""Paths shared by benchmark workspaces.

Code lives under ``benchmark/`` and moves with the current worktree. Data
(curated corpora, runs with ground truth or judge verdicts) lives under
``BENCHMARK_DATA_DIR`` so it survives worktrees; a rebase or a cleanup would
otherwise lose it. Every reader or writer of results or corpora goes through
``CORPORA_DIR`` and ``RUNS_DIR``.

A run is named ``<candidate>-<phase>-<NNNN>``: the slug of the thing tested,
a phase in ``{poc, debug, testing, integration}``, and a 4-digit counter per
(candidate, phase) pair, so runs sort by candidate, then phase, then id.
"""

from __future__ import annotations

import os
import re
from pathlib import Path

# ── Code (worktree) ─────────────────────────────────────────────────

BENCHMARK_CODE_DIR = Path(__file__).resolve().parent.parent
"""Holds code and versioned prompt templates only, never results."""


# ── Data (AppData, survives worktrees) ──────────────────────────────

def _resolve_data_dir() -> Path:
    override = os.environ.get("DECKLE_BENCHMARK_DIR")
    if override:
        return Path(override)
    localappdata = os.environ.get("LOCALAPPDATA")
    if not localappdata:
        return BENCHMARK_CODE_DIR
    return Path(localappdata) / "Deckle" / "benchmark"


BENCHMARK_DATA_DIR = _resolve_data_dir()

def _resolve_deckle_data_dir() -> Path:
    localappdata = os.environ.get("LOCALAPPDATA")
    if localappdata:
        return Path(localappdata) / "Deckle"
    # Keep the fallback coherent with _resolve_data_dir without making a
    # DECKLE_BENCHMARK_DIR override silently relocate app telemetry.
    return BENCHMARK_CODE_DIR


DECKLE_DATA_DIR = _resolve_deckle_data_dir()
"""Benchmarks read the app's own data root to import the telemetry it produces."""

DECKLE_TELEMETRY_DIR = DECKLE_DATA_DIR / "telemetry"

CORPORA_DIR = BENCHMARK_DATA_DIR / "corpora"
"""One subfolder per corpus slug, each with its enriched ``corpus.jsonl``."""

RUNS_DIR = BENCHMARK_DATA_DIR / "runs"


# ── Models (one shared store) ───────────────────────────────────────

def _resolve_models_dir() -> Path:
    override = os.environ.get("DECKLE_MODELS_DIR")
    if override:
        return Path(override)
    # Heavy models live in the app data root, outside any worktree, so
    # worktrees never duplicate them (same folder as AppPaths.ModelsDirectory).
    return DECKLE_DATA_DIR / "models"


MODELS_DIR = _resolve_models_dir()


# ── Helpers ─────────────────────────────────────────────────────────

_ID_SUFFIX_RE = re.compile(r"^\d{4}$")


def next_run_id(model: str, phase: str) -> int:
    """Return the next 4-digit id for (model, phase), starting at 1.

    Parse by the ``{model}-{phase}-`` prefix, not a global regex: ``model``
    may contain hyphens (``voxtral-llamacpp-mini3b-q8``). A parse that
    misses always returns 1, and the next ``mkdir(exist_ok=True)`` plus a
    ``"w"`` write silently overwrites an existing run."""
    if not RUNS_DIR.exists():
        return 1
    prefix = f"{model}-{phase}-"
    used = []
    for d in RUNS_DIR.iterdir():
        if not d.is_dir():
            continue
        if not d.name.startswith(prefix):
            continue
        suffix = d.name[len(prefix):]
        if _ID_SUFFIX_RE.match(suffix):
            used.append(int(suffix))
    return (max(used) + 1) if used else 1


def make_run_dir(model: str, phase: str, *, create: bool = True) -> Path:
    n = next_run_id(model, phase)
    run_dir = RUNS_DIR / f"{model}-{phase}-{n:04d}"
    if create:
        run_dir.mkdir(parents=True, exist_ok=True)
    return run_dir


def corpus_dir(slug: str) -> Path:
    return CORPORA_DIR / slug
