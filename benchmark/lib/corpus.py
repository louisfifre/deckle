"""Lecture des corpora pour le bench (layout v2).

Un corpus est un dossier sous ``benchmark/corpora/<slug>/`` qui contient :

  - ``corpus.jsonl`` : une ligne par sample, schéma payload Deckle telemetry
    (cf. corpus_asr dans Deckle.Diagnostics.Telemetry). Champs obligatoires
    utilisés ici : ``transcription_id``, ``audio_file``, ``text`` (réf
    Whisper large-v3), ``duration_seconds``, ``tier``.
  - ``<audio_file>`` : un WAV par sample, nom exact référencé dans
    ``payload.audio_file``.

Les corpora sont **gitignorés** : chaque utilisateur du bench amène ses
propres samples (typiquement extraits de ``%LOCALAPPDATA%\\Deckle\\telemetry\\``).
On ne distribue pas d'audio privé via Git.

Le corpus est traité en lecture seule par les benches — on ne touche
jamais aux fichiers sources, seulement aux résultats sous
``benchmark/runs/<run-id>/``.

Pourquoi un module séparé : ce loader est appelé par tous les benches
(voxtral-poc, whisper-stability futur, etc.) donc il vit en lib/ — pas
en duplication dans chaque bench.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path


BENCHMARK_DIR = Path(__file__).resolve().parent.parent
CORPORA_DIR = BENCHMARK_DIR / "corpora"


@dataclass(frozen=True)
class Sample:
    """Un sample du corpus, prêt à être consommé par une Source."""
    id: str
    audio_path: Path
    duration_s: float
    tier: str
    reference_text: str
    reference_words: int


def available() -> list[str]:
    """Liste les slugs de corpora dispo sur la machine. Pratique pour
    l'erreur ``corpus introuvable`` ou un menu CLI."""
    if not CORPORA_DIR.exists():
        return []
    return sorted(p.name for p in CORPORA_DIR.iterdir()
                  if p.is_dir() and (p / "corpus.jsonl").exists())


def load(slug: str) -> list[Sample]:
    """Charge un corpus depuis ``corpora/<slug>/corpus.jsonl``.

    Trie par durée croissante (utile pour le bench : on commence par les
    petits samples, le pipeline se réchauffe avant les longs). Filtre
    silencieusement les samples dont l'audio est introuvable — un corpus
    peut être partiel (ex. user a supprimé un WAV pour tester).
    """
    corpus_dir = CORPORA_DIR / slug
    jsonl_path = corpus_dir / "corpus.jsonl"
    if not jsonl_path.exists():
        raise FileNotFoundError(
            f"corpus {slug!r} introuvable : attendu {jsonl_path}\n"
            f"  Corpora disponibles sur cette machine : {available() or '<aucun>'}\n"
            f"  Les corpora ne sont PAS versionnés (gitignored). Tu dois "
            f"déposer tes propres samples sous {CORPORA_DIR}\\<slug>\\."
        )

    samples: list[Sample] = []
    for line in jsonl_path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line:
            continue
        entry = json.loads(line)
        payload = entry["payload"]
        wav_path = corpus_dir / payload["audio_file"]
        if not wav_path.exists():
            continue
        samples.append(Sample(
            id=str(payload["transcription_id"]),
            audio_path=wav_path,
            duration_s=float(payload["duration_seconds"]),
            tier=str(payload["tier"]),
            reference_text=str(payload["text"]),
            reference_words=int(payload["text_words"]),
        ))
    samples.sort(key=lambda s: s.duration_s)
    return samples
