"""Lit le smoke test voxtral-transformers-validation-0002 et imprime
les sorties textuelles par sample × régime pour validation qualitative.

Critère d'acceptation du palier 2 (smoke test 1 sample × 7 régimes) :
chaque régime doit produire une sortie qualitativement cohérente avec
son intention (T1 transcription, T2 verbatim, T3 anglais, T4 résumé,
T5 description du ton, T6 transcription + étiquette).
"""

from __future__ import annotations

import io
import json
import os
import sys
from pathlib import Path

if sys.stdout.encoding.lower() != "utf-8":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

RUN = Path(os.environ["LOCALAPPDATA"]) / "Deckle" / "benchmark" / "runs" / "voxtral-transformers-validation-0002"


def main() -> int:
    rows = []
    with (RUN / "results.jsonl").open("r", encoding="utf-8") as f:
        for line in f:
            rows.append(json.loads(line))

    by_sample: dict[str, list[dict]] = {}
    for r in rows:
        by_sample.setdefault(r["audio_id"], []).append(r)

    for aid, sample_rows in by_sample.items():
        first = sample_rows[0]
        print("=" * 100)
        print(f"Sample {aid[:8]}  ({first['audio_seconds']:.1f}s, tier={first.get('tier','?')})")
        print(f"  ref whisper : {first['reference_text_whisper'][:120]}")
        print(f"  ref gemini  : {first['reference_text_gemini'][:120]}")
        print("=" * 100)
        for r in sample_rows:
            mode = r.get("extras", {}).get("mode", "?")
            instr = r.get("extras", {}).get("chat_instruction", "")
            print(f"\n--- {r['regime']:<16s}  mode={mode}")
            if instr:
                print(f"    instr : {instr[:140]}")
            print(f"    out  : {r['text'][:300]}")
            print(f"    wer={r['metrics']['wer']:.2f}  ratio={r['metrics']['word_count_ratio']:.2f}  generated_tokens={r.get('generated_tokens',-1)}")
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
