"""Construit un corpus ASR stratifié à partir de la télémétrie Deckle.

Le corpus de validation par défaut prend 30 samples répartis sur cinq buckets
de durée (very-short, short, medium, long, very-long-edge). Le but est de
produire un corpus d'évaluation réutilisable pour plusieurs backends ASR, pas
un corpus attaché à un modèle précis.

Source : ``%LOCALAPPDATA%\\Deckle\\telemetry\\``. Aucun audio versionné
côté Git — chaque machine apporte ses propres samples.

Stratification choisie 6 / 8 / 9 / 6 / 1 :

  | Bucket          | n     | range          | rationale                              |
  |-----------------|-------|----------------|----------------------------------------|
  | very-short      | 6/42  | < 5 s          | dictée note flash                      |
  | short           | 8/67  | 5–15 s         | dictée note normale (usage médian)     |
  | medium          | 9/164 | 15–60 s        | paragraphe ou idée structurée          |
  | long            | 6/95  | 60–300 s       | monologue ou réflexion suivie          |
  | very-long-edge  | 1/1   | 330 s          | unique sample > 300 s (edge case)      |

Sélection ``equal-spacing`` à l'intérieur de chaque bucket : tri par
durée croissante, pick aux indices ``round(len * i / N)`` pour
``i ∈ [0, N)``. Donne une couverture diverse plutôt que les N premiers
ou un échantillon aléatoire — reproductible, stable, et présente le
biais voulu (pas de bagarre entre samples très proches en durée).

Le ``corpus.jsonl`` produit suit la convention v2 du loader ``lib/corpus.py`` :
top-level ``timestamp / kind / session / payload``. Le payload conserve les
champs de télémétrie utiles : ``transcription_id``, ``audio_file``, ``text``,
``duration_seconds``, ``tier``, ``text_words``.

Usage::

    python benchmark/asr/build_corpus.py
    python benchmark/asr/build_corpus.py --corpus asr-val-30 --dry-run
    python benchmark/asr/build_corpus.py --corpus voxtral-val-30 --force
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


# ── Sources télémétrie ────────────────────────────────────────────────

DECKLE_TELEMETRY_DIR = paths.DECKLE_TELEMETRY_DIR
AUDIO_DIR            = DECKLE_TELEMETRY_DIR / "audio"
CORPUS_RAW_DIR       = DECKLE_TELEMETRY_DIR / "corpus" / "raw"

DEFAULT_CORPUS_SLUG = "asr-val-30"


# ── Schéma de stratification ──────────────────────────────────────────

@dataclass(frozen=True)
class Bucket:
    """Un seau de stratification ``durée → n samples à prendre``."""
    name:     str           # nom interne (very-short / short / …)
    pick_n:   int           # nombre de samples à prendre dans ce bucket
    duration_predicate: callable    # fonction qui matche les samples du bucket


BUCKETS: list[Bucket] = [
    Bucket("very-short",     6, lambda d: d < 5),
    Bucket("short",          8, lambda d: 5 <= d < 15),
    Bucket("medium",         9, lambda d: 15 <= d < 60),
    Bucket("long",           6, lambda d: 60 <= d < 300),
    Bucket("very-long-edge", 1, lambda d: d >= 300),
]
TARGET_TOTAL = sum(b.pick_n for b in BUCKETS)  # 30


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


# ── Lecture télémétrie ────────────────────────────────────────────────

def _load_telemetry() -> list[dict]:
    """Lit tous les ``corpus.jsonl`` du raw/<tier>/ et garde uniquement les
    samples dont le WAV existe encore sur disque. Dédup par
    ``transcription_id`` (au cas où un sample serait dans deux buckets,
    ce qui ne devrait pas arriver mais qu'on ne fait pas exploser)."""
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
            # Filtre robuste : ``audio_file`` peut être vide (transcription
            # sans ``RecordAudioCorpus`` actif côté Deckle, la ligne JSONL
            # existe mais aucun WAV associé). On exige donc explicitement
            # un fichier — ``exists()`` seul retournerait True sur le
            # dossier parent quand ``audio_file`` est "".
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
    """Pour chaque bucket, tri les candidats par durée croissante puis
    pick equal-spacing — indices ``round(len * i / N)`` pour
    ``i ∈ [0, N)``. Si un bucket a moins de candidats que ``pick_n``,
    on prend tout ce qu'il y a (perte silencieuse — affichée par la
    fonction principale)."""
    out: dict[str, list[dict]] = {}
    for b in BUCKETS:
        candidates = [s for s in samples if b.duration_predicate(s["duration_s"])]
        candidates.sort(key=lambda s: s["duration_s"])
        if len(candidates) <= b.pick_n:
            out[b.name] = candidates
            continue
        # Equal-spacing : tire N indices répartis sur [0, len-1].
        # ``round(len * i / N)`` au lieu de ``i * (len // N)`` parce que
        # c'est plus stable quand ``len`` n'est pas multiple de N (sinon
        # on perd les derniers indices systématiquement).
        n = len(candidates)
        picked_idx = [round(n * i / b.pick_n) for i in range(b.pick_n)]
        # Sécurité : si la formule donne deux fois le même index sur de
        # très petits N (improbable avec nos ratios mais soyons safe),
        # on dédupe en préservant l'ordre.
        seen_idx: set[int] = set()
        unique_idx: list[int] = []
        for idx in picked_idx:
            idx = min(idx, n - 1)
            if idx not in seen_idx:
                seen_idx.add(idx)
                unique_idx.append(idx)
        out[b.name] = [candidates[i] for i in unique_idx]
    return out


# ── Écriture ──────────────────────────────────────────────────────────

def _write_corpus(selection: dict[str, list[dict]], corpus_dir: Path) -> int:
    """Copie les WAVs sélectionnés vers ``corpora/<slug>/`` et écrit le
    ``corpus.jsonl`` consolidé. Chaque ligne JSONL réutilise l'entrée originale
    de la télémétrie et ajoute un champ :

      - ``payload.tier_validation`` : le bucket de stratification utilisé
        ici (très-short / short / medium / long / very-long-edge). On ne
        touche pas au ``payload.tier`` original qui reste l'info Deckle
        word-count.
    """
    jsonl_path = corpus_dir / "corpus.jsonl"
    n = 0
    with jsonl_path.open("w", encoding="utf-8") as fout:
        for bucket_name in (b.name for b in BUCKETS):
            for s in selection.get(bucket_name, []):
                # Copie WAV à plat. ``copyfile`` plutôt que ``copy2`` :
                # ce dernier propage les permissions via ``os.stat`` sur
                # le dossier source, ce qui plante sous sandbox restrict
                # quand le dossier source est hors du worktree. On n'a
                # pas besoin des métadonnées (mtime) pour un corpus de
                # bench.
                dst_wav = corpus_dir / s["audio_file"]
                shutil.copyfile(s["audio_path"], dst_wav)

                # Enrichit le payload original
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
