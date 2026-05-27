"""Comparaison rapide BF16 vs Q4_K_M sur T1_baseline.

Lit les results.jsonl des runs voxtral-transformers-validation-0001
(BF16) et voxtral-poc-0001 (Q4_K_M), agrège les métriques objectives,
affiche les 3 samples critiques annotés par Louis dans
voxtral-val-30-notes.json.
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
Q4_RUN   = "voxtral-poc-0001"
REGIME   = "T1_baseline"


def load(run_name: str, regime: str = REGIME) -> dict[str, dict]:
    rows = {}
    path = RUNS / run_name / "results.jsonl"
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            r = json.loads(line)
            if r.get("regime") == regime:
                rows[r["audio_id"]] = r
    return rows


def main() -> int:
    bf16 = load(BF16_RUN)
    q4   = load(Q4_RUN)

    print(f"BF16 T1   : {len(bf16)} samples ({BF16_RUN})")
    print(f"Q4_K_M T1 : {len(q4)} samples ({Q4_RUN})")
    common = sorted(set(bf16.keys()) & set(q4.keys()))
    print(f"Communs   : {len(common)} samples\n")

    wer_bf16   = [bf16[k]["metrics"]["wer"] for k in common if bf16[k]["metrics"]["wer"] is not None]
    wer_q4     = [q4[k]["metrics"]["wer"]   for k in common if q4[k]["metrics"]["wer"] is not None]
    ratio_bf16 = [bf16[k]["metrics"]["word_count_ratio"] for k in common if bf16[k]["metrics"]["word_count_ratio"] is not None]
    ratio_q4   = [q4[k]["metrics"]["word_count_ratio"]   for k in common if q4[k]["metrics"]["word_count_ratio"] is not None]
    rtf_bf16   = [bf16[k]["rtf"] for k in common if bf16[k]["rtf"] > 0]
    rtf_q4     = [q4[k]["rtf"]   for k in common if q4[k]["rtf"] > 0]

    print(f"{'metric':<26s}  {'BF16':>8s}  {'Q4_K_M':>8s}  {'delta':>8s}")
    print("-" * 60)
    fmt = "{:<26s}  {:>8.3f}  {:>8.3f}  {:>+8.3f}"
    print(fmt.format("WER median",       statistics.median(wer_bf16),   statistics.median(wer_q4),   statistics.median(wer_bf16)   - statistics.median(wer_q4)))
    print(fmt.format("WER mean",         statistics.mean(wer_bf16),     statistics.mean(wer_q4),     statistics.mean(wer_bf16)     - statistics.mean(wer_q4)))
    print(fmt.format("WER stdev",        statistics.stdev(wer_bf16),    statistics.stdev(wer_q4),    statistics.stdev(wer_bf16)    - statistics.stdev(wer_q4)))
    print(fmt.format("word_count_ratio med", statistics.median(ratio_bf16), statistics.median(ratio_q4), statistics.median(ratio_bf16) - statistics.median(ratio_q4)))
    print(fmt.format("RTF median",       statistics.median(rtf_bf16),   statistics.median(rtf_q4),   statistics.median(rtf_bf16)   - statistics.median(rtf_q4)))
    print(fmt.format("RTF mean",         statistics.mean(rtf_bf16),     statistics.mean(rtf_q4),     statistics.mean(rtf_bf16)     - statistics.mean(rtf_q4)))

    print()
    print("=== Samples critiques (notes Louis) ===")

    # Trois samples où les notes Louis pointent des erreurs spécifiques en Q4_K_M.
    # dcad692a : sample 1.7s avec contenu réel "Et toujours douter un peu."
    # e6db36e7 : 'si tu t'autorises' vs 'si je t'autorise', 0.3 vs 0.3.1
    # 701ce47a : VRAM oublié, 8K oublié
    critical = [
        ("dcad692a54fd452cbfb174ca9899deba", "Et toujours douter un peu."),
        ("e6db36e764764be78f7514f5852fac32", "if je t'autorise + 0.3.1"),
        ("701ce47a167f40f1b49c3a32a446358b", "VRAM + 8K"),
    ]

    for cid, hint in critical:
        if cid not in common:
            print(f"\n--- {cid[:8]} : NOT FOUND ---")
            continue
        b = bf16[cid]
        q = q4[cid]
        print(f"\n--- {cid[:8]} ({b['audio_seconds']:.1f}s) — Louis : {hint} ---")
        print(f"REF  : {b['reference_text_gemini'][:250]}")
        print()
        print(f"BF16 : {b['text'][:250]}")
        print(f"Q4KM : {q['text'][:250]}")
        print(f"WER  BF16 {b['metrics']['wer']:.3f}  |  Q4_K_M {q['metrics']['wer']:.3f}")

    print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
