"""Load benchmark corpora.

A corpus is a folder ``corpora/<slug>/`` holding ``corpus.jsonl`` (one
Deckle telemetry payload per sample) and one WAV per sample, named by
``payload.audio_file``.

Corpora hold the user's own voice, so they are never versioned: each
machine brings its own samples, from its Deckle telemetry or captured
fresh. Benches treat a corpus as read-only and write only under
``RUNS_DIR``.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

from lib import paths


CORPORA_DIR = paths.CORPORA_DIR


@dataclass(frozen=True)
class Sample:
    """``reference_text`` is the Whisper large-v3 transcript from telemetry.
    ``reference_text_gemini`` stays empty until a Gemini ground-truth pass
    enriches the corpus; a bench that scores against it fails loudly on an
    empty value rather than fall back to the Whisper reference.
    """
    id: str
    audio_path: Path
    duration_s: float
    tier: str
    reference_text: str
    reference_words: int
    reference_text_gemini: str = ""


def available() -> list[str]:
    if not CORPORA_DIR.exists():
        return []
    return sorted(p.name for p in CORPORA_DIR.iterdir()
                  if p.is_dir() and (p / "corpus.jsonl").exists())


def load(slug: str) -> list[Sample]:
    """Sorted by ascending duration, so the pipeline warms up on short
    samples before the long ones. A sample whose WAV is missing is skipped:
    a corpus may be partial.
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
            reference_text_gemini=str(payload.get("reference_text_gemini", "")),
        ))
    samples.sort(key=lambda s: s.duration_s)
    return samples
