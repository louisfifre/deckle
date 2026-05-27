"""Agrégation des verdicts judge d'un run pour cartographier les modes d'échec.

Lit results.jsonl d'un run et produit deux vues complémentaires :

  1. **Verdicts triés par sévérité** — les N rows où l'axe demandé est le
     plus bas, avec leur verdict prose. Utile pour comprendre vite ce
     qui pèche.
  2. **Comptage de mots-clés sémantiques** — fréquence d'expressions
     diagnostiques récurrentes dans les verdicts (« hallucine », « omet »,
     « confond », « lisse », « paraphrase », etc.). Donne une vue macro
     des patterns d'erreur dominants par régime.

Usage :
  python aggregate_verdicts.py voxtral-transformers-validation-0004
  python aggregate_verdicts.py voxtral-transformers-validation-0004 --axis fidelite_signal --threshold 80 --top 30
"""

from __future__ import annotations

import argparse
import io
import json
import os
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

if sys.stdout.encoding.lower() != "utf-8":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

RUNS = Path(os.environ["LOCALAPPDATA"]) / "Deckle" / "benchmark" / "runs"

# Vocabulaire diagnostique observé dans les verdicts Gemini sur les
# benchs voxtral-validation. Chaque entrée est (label, regex) où le
# regex est appliqué case-insensitive sur le verdict. À enrichir au fil
# des observations.
KEYWORDS = [
    ("hallucination",      r"halluci|invent|fabriqu|inexistant|absent du signal|absent du son"),
    ("paraphrase",         r"paraphras|reformul|réécri"),
    ("lissage",            r"lisse|liss[eé]|adouci|standardis"),
    ("omission",           r"omet|oubl|manqu|absent de l['e]hypothèse"),
    ("ajout",              r"ajout|introduit|surajoute"),
    ("pronom",             r"pronom|je\b.*tu|tu\b.*je|inversion|inverse"),
    ("anglicisme",         r"anglicism|anglais|english|terme technique"),
    ("registre",           r"registre|formel|informel|hésitant|ton"),
    ("traduction-faiblesse", r"traduction|traduit|english"),
    ("régime-violation",   r"violation|ne respecte pas|non conforme|hors régime"),
    ("ref-suspecte",       r"référence|whisper|suspect|hallucination de la référence"),
    ("audio-bref",         r"trop bref|trop court|trop bref pour"),
    ("audio-silence",      r"silence|soupir|hmm|bruit|bouche"),
]


def load_rows(run_name: str) -> list[dict]:
    path = RUNS / run_name / "results.jsonl"
    if not path.exists():
        print(f"FATAL : results.jsonl introuvable sous {path}", file=sys.stderr)
        sys.exit(1)
    rows = []
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            rows.append(json.loads(line))
    return rows


def hardest_rows(rows: list[dict], axis: str, threshold: int, top: int) -> list[dict]:
    """Retourne les rows avec axis < threshold, triées par axe croissant."""
    candidates = []
    for r in rows:
        if not r.get("ok"):
            continue
        judge = r.get("judge") or {}
        if not judge.get("parse_ok"):
            continue
        axes = judge.get("axes") or {}
        score = axes.get(axis)
        if score is None or score >= threshold:
            continue
        candidates.append((score, r))
    candidates.sort(key=lambda t: t[0])
    return [r for _, r in candidates[:top]]


def keyword_counts(rows: list[dict]) -> dict[str, Counter]:
    """Pour chaque régime, compte les occurrences des mots-clés
    diagnostiques dans les verdicts."""
    by_regime: dict[str, Counter] = defaultdict(Counter)
    for r in rows:
        regime = r["regime"]
        judge = r.get("judge") or {}
        verdict = (judge.get("verdict") or "").lower()
        if not verdict:
            continue
        for label, pattern in KEYWORDS:
            if re.search(pattern, verdict):
                by_regime[regime][label] += 1
    return by_regime


def print_hardest(rows: list[dict], axis: str, threshold: int, top: int) -> None:
    hard = hardest_rows(rows, axis, threshold, top)
    print(f"\n{'='*80}")
    print(f"Top {len(hard)} rows avec axe `{axis}` < {threshold} (triées par axe croissant)")
    print(f"{'='*80}\n")
    if not hard:
        print(f"  Aucune row sous le seuil — bench très bon sur cet axe.")
        return
    for r in hard:
        aid = r["audio_id"][:8]
        axes = r["judge"]["axes"]
        verdict = r["judge"].get("verdict", "").strip()
        print(f"  [{aid}] {r['audio_seconds']:5.1f}s {r['regime']:<16s} "
              f"axes={axes.get('fidelite_signal','-')}/{axes.get('proprete','-')}/"
              f"{axes.get('absence_hallucination','-')}/{axes.get('regime_respecte','-')}")
        print(f"         ref  : {r['reference_text_gemini'][:140]}")
        print(f"         hyp  : {r['text'][:140]}")
        print(f"         verdict : {verdict[:200]}")
        print()


def print_keyword_table(rows: list[dict]) -> None:
    print(f"\n{'='*80}")
    print("Fréquence des mots-clés diagnostiques dans les verdicts, par régime")
    print(f"{'='*80}\n")
    counts = keyword_counts(rows)
    if not counts:
        print("  Aucun verdict exploitable.")
        return
    regimes = sorted(counts.keys())
    labels = [lab for lab, _ in KEYWORDS]
    col_w = max(len(l) for l in labels) + 2
    header = f"{'pattern':<{col_w}s}" + "".join(f"{r:>16s}" for r in regimes)
    print("  " + header)
    print("  " + "-" * len(header))
    for label in labels:
        row = f"{label:<{col_w}s}" + "".join(f"{counts[r].get(label, 0):>16d}" for r in regimes)
        print("  " + row)
    print()
    print("  (Les patterns sont des heuristiques regex — vérifier sur quelques verdicts au cas par cas.)")


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("run_name", help="Nom du run sous %%LOCALAPPDATA%%\\Deckle\\benchmark\\runs\\")
    p.add_argument("--axis", default="fidelite_signal",
                   choices=["fidelite_signal", "proprete", "absence_hallucination", "regime_respecte"],
                   help="Axe judge pour le tri des outliers (défaut : fidelite_signal)")
    p.add_argument("--threshold", type=int, default=70,
                   help="Seuil sous lequel l'axe est jugé bas (défaut : 70)")
    p.add_argument("--top", type=int, default=20,
                   help="Nombre maximum d'outliers à lister (défaut : 20)")
    args = p.parse_args()

    rows = load_rows(args.run_name)
    print(f"run : {args.run_name}")
    print(f"rows lues : {len(rows)}")
    print(f"rows avec judge : {sum(1 for r in rows if (r.get('judge') or {}).get('parse_ok'))}")

    print_hardest(rows, args.axis, args.threshold, args.top)
    print_keyword_table(rows)
    return 0


if __name__ == "__main__":
    sys.exit(main())
