"""Analyse de la grande passe BF16 Mini 3B voxtral-validation 0001.

Lit voxtral-transformers-validation-0001/results.jsonl et produit :
  - Synthèse par régime (T1-T5) : N valides, WER médian/moyenne/stdev,
    ratio médian, RTF médian, axes judge moyens.
  - Liste des dégénérations (WER > 2.0).
  - Comparaison cross-runs T1 vs Q8_0 et Q4_K_M 24B.

Note : T6_sys_prompt n'est pas implémenté côté Transformers, toutes ses
rows sont en FAIL — exclues de l'analyse.
"""

from __future__ import annotations

import io
import json
import os
import statistics
import sys
from pathlib import Path

if sys.stdout.encoding.lower() != "utf-8":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

RUNS = Path(os.environ["LOCALAPPDATA"]) / "Deckle" / "benchmark" / "runs"

BF16_RUN = "voxtral-transformers-validation-0001"
Q8_RUN   = "voxtral-llamacpp-mini3b-q8-validation-0001"
Q4_RUN   = "voxtral-poc-0001"
REGIMES  = ["T1_baseline", "T2_verbatim", "T3_translate", "T4_summary", "T5_qa_register"]


def load_rows(run_name: str) -> list[dict]:
    path = RUNS / run_name / "results.jsonl"
    rows = []
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            rows.append(json.loads(line))
    return rows


def fmt_stat(label: str, vals: list[float], width: int = 8) -> str:
    if not vals:
        return f"{label:<28s}  n=0"
    med = statistics.median(vals)
    mean = statistics.mean(vals)
    stdev = statistics.stdev(vals) if len(vals) > 1 else 0.0
    return f"{label:<28s}  med {med:>{width}.3f}  mean {mean:>{width}.3f}  stdev {stdev:>{width}.3f}  n={len(vals)}"


def regime_summary(rows: list[dict]) -> None:
    print(f"\n{'='*78}")
    print(f"GRILLE BF16 Mini 3B — par régime (run : {BF16_RUN})")
    print(f"{'='*78}\n")

    for regime in REGIMES:
        ok_rows = [r for r in rows if r["regime"] == regime and r["ok"]]
        fail_rows = [r for r in rows if r["regime"] == regime and not r["ok"]]
        print(f"--- {regime}  (ok={len(ok_rows)}, fail={len(fail_rows)})")

        wer    = [r["metrics"]["wer"] for r in ok_rows if r["metrics"]["wer"] is not None]
        ratio  = [r["metrics"]["word_count_ratio"] for r in ok_rows if r["metrics"]["word_count_ratio"] is not None]
        rtf    = [r["rtf"] for r in ok_rows if r["rtf"] > 0]

        print("  " + fmt_stat("WER",   wer))
        print("  " + fmt_stat("ratio", ratio))
        print("  " + fmt_stat("RTF",   rtf))

        judged = [r for r in ok_rows if (r.get("judge") or {}).get("parse_ok")]
        if judged:
            fid  = [r["judge"]["axes"]["fidelite_signal"] for r in judged]
            prop = [r["judge"]["axes"]["proprete"] for r in judged]
            hall = [r["judge"]["axes"]["absence_hallucination"] for r in judged]
            reg  = [r["judge"]["axes"]["regime_respecte"] for r in judged]
            print(f"  judge axes (mean)         fid={statistics.mean(fid):5.1f}  prop={statistics.mean(prop):5.1f}  hall={statistics.mean(hall):5.1f}  reg={statistics.mean(reg):5.1f}  n={len(judged)}")
        print()


def degenerations(rows: list[dict]) -> None:
    print(f"\n{'='*78}")
    print("DÉGÉNÉRATIONS (WER > 2.0 → chat-mode ou hallucination longue)")
    print(f"{'='*78}\n")
    bad = [r for r in rows if r["ok"] and r["metrics"]["wer"] is not None and r["metrics"]["wer"] > 2.0]
    if not bad:
        print("  Aucune row WER > 2.0.")
        return
    bad.sort(key=lambda r: r["metrics"]["wer"], reverse=True)
    for r in bad[:20]:
        aid = r["audio_id"][:8]
        print(f"  [{aid}] {r['audio_seconds']:5.1f}s {r['regime']:<16s} wer={r['metrics']['wer']:6.2f} ratio={r['metrics']['word_count_ratio']:5.2f}")
        print(f"         ref  : {r['reference_text_gemini'][:120]}")
        print(f"         hyp  : {r['text'][:120]}")
        print()
    print(f"  Total : {len(bad)} rows WER > 2.0\n")


def compare_t1_t2(rows: list[dict]) -> None:
    print(f"\n{'='*78}")
    print("T1_baseline vs T2_verbatim — signal d'amélioration prompt")
    print(f"{'='*78}\n")
    by_aid = {}
    for r in rows:
        if not r["ok"]: continue
        if r["regime"] not in ("T1_baseline", "T2_verbatim"): continue
        by_aid.setdefault(r["audio_id"], {})[r["regime"]] = r
    paired = [(d["T1_baseline"], d["T2_verbatim"]) for d in by_aid.values() if "T1_baseline" in d and "T2_verbatim" in d]
    if not paired:
        print("  Aucune paire T1/T2.")
        return
    deltas = [t2["metrics"]["wer"] - t1["metrics"]["wer"] for t1, t2 in paired if t1["metrics"]["wer"] is not None and t2["metrics"]["wer"] is not None]
    t1_wins = sum(1 for d in deltas if d > 0.05)
    t2_wins = sum(1 for d in deltas if d < -0.05)
    tied    = sum(1 for d in deltas if abs(d) <= 0.05)
    print(f"  paires comparables : {len(deltas)}")
    print(f"  T1 mieux (delta > +0.05) : {t1_wins}")
    print(f"  T2 mieux (delta < -0.05) : {t2_wins}")
    print(f"  équivalents              : {tied}")
    print(f"  delta WER médian (T2-T1) : {statistics.median(deltas):+.3f}")
    print(f"  delta WER moyen (T2-T1)  : {statistics.mean(deltas):+.3f}")


def cross_run_t1(bf16_rows: list[dict]) -> None:
    print(f"\n{'='*78}")
    print("CROSS-RUN T1_baseline — BF16 vs Q8_0 vs Q4_K_M 24B")
    print(f"{'='*78}\n")

    def t1_wer_by_aid(run_name: str) -> dict[str, float]:
        try:
            rows = load_rows(run_name)
        except FileNotFoundError:
            return {}
        return {
            r["audio_id"]: r["metrics"]["wer"]
            for r in rows
            if r["regime"] == "T1_baseline" and r.get("ok") and r["metrics"]["wer"] is not None
        }

    bf16 = {r["audio_id"]: r["metrics"]["wer"] for r in bf16_rows if r["regime"] == "T1_baseline" and r["ok"] and r["metrics"]["wer"] is not None}
    q8   = t1_wer_by_aid(Q8_RUN)
    q4   = t1_wer_by_aid(Q4_RUN)

    common = sorted(set(bf16) & set(q8) & set(q4))
    print(f"  samples communs (BF16 ∩ Q8 ∩ Q4) : {len(common)}\n")

    def stats_label(label: str, vals: list[float]):
        med   = statistics.median(vals)
        mean  = statistics.mean(vals)
        stdev = statistics.stdev(vals) if len(vals) > 1 else 0.0
        print(f"  {label:<28s}  med {med:.3f}  mean {mean:.3f}  stdev {stdev:.3f}")

    stats_label("BF16 Mini 3B",            [bf16[k] for k in common])
    stats_label("Q8_0 Mini 3B (llamacpp)", [q8[k]   for k in common])
    stats_label("Q4_K_M Small 24B",        [q4[k]   for k in common])


def main() -> int:
    rows = load_rows(BF16_RUN)
    total = len(rows)
    ok    = sum(1 for r in rows if r["ok"])
    fail  = total - ok
    print(f"run : {BF16_RUN}")
    print(f"rows : {total}  (ok={ok}, fail={fail})")

    regime_summary(rows)
    degenerations(rows)
    compare_t1_t2(rows)
    cross_run_t1(rows)

    return 0


if __name__ == "__main__":
    sys.exit(main())
