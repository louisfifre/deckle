"""Construit un corpus ASR stratifié à partir de la télémétrie Deckle.

``_parse_args`` shows the first line as the CLI description. The corpus
serves every ASR backend, not one model. Its samples hold the user's own voice, so no audio
is ever versioned: each machine builds its corpus from its own telemetry.

Duration buckets follow dictation lengths, from a quick note to a
monologue, plus the rare clip past 300 s as an edge case. Each line of
``corpus.jsonl`` keeps the telemetry entry whole, the shape
``lib/corpus.py`` reads.
"""

from __future__ import annotations

import argparse
import json
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path

BENCHMARK_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(BENCHMARK_DIR.parent))

from lib._base_compat import _ensure_stdout_utf8
from lib import paths


# ── Telemetry sources ─────────────────────────────────────────────────

DECKLE_TELEMETRY_DIR = paths.DECKLE_TELEMETRY_DIR
AUDIO_DIR            = DECKLE_TELEMETRY_DIR / "audio"
CORPUS_RAW_DIR       = DECKLE_TELEMETRY_DIR / "corpus" / "raw"

DEFAULT_CORPUS_SLUG = "asr-val-30"


# ── Stratification schema ─────────────────────────────────────────────

@dataclass(frozen=True)
class Bucket:
    name:     str
    pick_n:   int
    duration_predicate: callable


BUCKETS: list[Bucket] = [
    Bucket("very-short",     6, lambda d: d < 5),
    Bucket("short",          8, lambda d: 5 <= d < 15),
    Bucket("medium",         9, lambda d: 15 <= d < 60),
    Bucket("long",           6, lambda d: 60 <= d < 300),
    Bucket("very-long-edge", 1, lambda d: d >= 300),
]
TARGET_TOTAL = sum(b.pick_n for b in BUCKETS)


# ── Pipeline ──────────────────────────────────────────────────────────

def main() -> int:
    _ensure_stdout_utf8()
    args = _parse_args()

    if not AUDIO_DIR.exists() or not CORPUS_RAW_DIR.exists():
        print(f"FATAL : télémétrie Deckle introuvable.\n"
              f"  attendu audio = {AUDIO_DIR}\n"
              f"  attendu raw   = {CORPUS_RAW_DIR}", file=sys.stderr)
        return 2

    all_samples = _load_telemetry()
    print(f"Lu {len(all_samples)} samples de la télémétrie Deckle.")

    selection = _stratify(all_samples)
    n_picked = sum(len(v) for v in selection.values())
    print(f"Sélection stratifiée : {n_picked} samples sur {TARGET_TOTAL} attendus.")
    print()
    for b in BUCKETS:
        picked = selection.get(b.name, [])
        if picked:
            durations = sorted(s["duration_s"] for s in picked)
            print(f"  {b.name:<16} n={len(picked):>2}  "
                  f"durées={[f'{d:.1f}s' for d in durations]}")
        else:
            print(f"  {b.name:<16} n=0  ⚠ aucun candidat dans la télémétrie")

    if n_picked < TARGET_TOTAL:
        print(f"\n⚠ Sélection partielle : {n_picked} < {TARGET_TOTAL}. "
              f"Continue quand même (la stratification absorbe les buckets vides).",
              file=sys.stderr)

    if args.dry_run:
        print("\n(dry-run, rien écrit)")
        return 0

    corpus_dir = paths.corpus_dir(args.corpus)
    if corpus_dir.exists() and not args.force:
        print(f"\nFATAL : {corpus_dir} existe déjà. --force pour écraser.",
              file=sys.stderr)
        return 2
    if corpus_dir.exists():
        shutil.rmtree(corpus_dir)
    corpus_dir.mkdir(parents=True)

    n_written = _write_corpus(selection, corpus_dir)
    print(f"\n✓ corpus écrit sous {corpus_dir} ({n_written} samples).")
    return 0


# ── Telemetry reading ─────────────────────────────────────────────────

def _load_telemetry() -> list[dict]:
    """Keep only samples whose WAV is still on disk, once per
    ``transcription_id`` even if two tier folders list it."""
    seen: set[str] = set()
    out: list[dict] = []
    for jsonl in sorted(CORPUS_RAW_DIR.glob("*/corpus.jsonl")):
        for line in jsonl.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if not line:
                continue
            entry = json.loads(line)
            p = entry["payload"]
            tid = str(p["transcription_id"])
            if tid in seen:
                continue
            # ``audio_file`` is empty when Deckle recorded the line without
            # its audio, and ``AUDIO_DIR / ""`` is the folder itself, which
            # ``exists()`` would accept.
            audio_file = str(p.get("audio_file") or "").strip()
            if not audio_file:
                continue
            wav = AUDIO_DIR / audio_file
            if not wav.is_file():
                continue
            seen.add(tid)
            out.append({
                "raw_entry":   entry,
                "transcription_id": tid,
                "audio_file":  p["audio_file"],
                "audio_path":  wav,
                "duration_s":  float(p["duration_seconds"]),
                "tier":        str(p.get("tier", "")),
                "text":        str(p["text"]),
                "text_words":  int(p["text_words"]),
            })
    return out


# ── Stratification ────────────────────────────────────────────────────

def _stratify(samples: list[dict]) -> dict[str, list[dict]]:
    """Pick equally spaced samples across each bucket sorted by duration:
    the whole range is covered and the pick is reproducible, where the
    first N would cluster and a random pick would not repeat. A bucket
    short of ``pick_n`` gives everything it has; ``main`` reports the gap."""
    out: dict[str, list[dict]] = {}
    for b in BUCKETS:
        candidates = [s for s in samples if b.duration_predicate(s["duration_s"])]
        candidates.sort(key=lambda s: s["duration_s"])
        if len(candidates) <= b.pick_n:
            out[b.name] = candidates
            continue
        # ``round(n * i / N)``, not ``i * (n // N)``: the latter never
        # reaches the longest samples when n is not a multiple of N.
        n = len(candidates)
        picked_idx = [round(n * i / b.pick_n) for i in range(b.pick_n)]
        seen_idx: set[int] = set()
        unique_idx: list[int] = []
        for idx in picked_idx:
            idx = min(idx, n - 1)
            if idx not in seen_idx:
                seen_idx.add(idx)
                unique_idx.append(idx)
        out[b.name] = [candidates[i] for i in unique_idx]
    return out


# ── Writing ───────────────────────────────────────────────────────────

def _write_corpus(selection: dict[str, list[dict]], corpus_dir: Path) -> int:
    """``payload.tier_validation`` records the duration bucket; the original
    ``payload.tier`` stays Deckle's word-count tier."""
    jsonl_path = corpus_dir / "corpus.jsonl"
    n = 0
    with jsonl_path.open("w", encoding="utf-8") as fout:
        for bucket_name in (b.name for b in BUCKETS):
            for s in selection.get(bucket_name, []):
                # ``copyfile``, not ``copy2``: copying metadata fails under a
                # restricted sandbox when the source sits outside the
                # worktree, and a corpus needs no mtime.
                dst_wav = corpus_dir / s["audio_file"]
                shutil.copyfile(s["audio_path"], dst_wav)

                entry = json.loads(json.dumps(s["raw_entry"]))   # deep-copy via JSON
                entry["payload"]["tier_validation"] = bucket_name

                fout.write(json.dumps(entry, ensure_ascii=False) + "\n")
                fout.flush()
                n += 1
    return n


# ── Sub-builders ──────────────────────────────────────────────────────

def _parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--corpus", default=DEFAULT_CORPUS_SLUG,
                   help=f"Slug du corpus à écrire (défaut : {DEFAULT_CORPUS_SLUG}).")
    p.add_argument("--dry-run", action="store_true",
                   help="Affiche la sélection mais n'écrit rien.")
    p.add_argument("--force",   action="store_true",
                   help="Écrase un corpus existant sans demander.")
    return p.parse_args()


if __name__ == "__main__":
    sys.exit(main())
