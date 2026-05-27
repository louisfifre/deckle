"""Inspecte les outputs ambigus du smoke test palier 3."""

from __future__ import annotations
import io, json, os, sys
from pathlib import Path

if sys.stdout.encoding.lower() != "utf-8":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

RUN = Path(os.environ["LOCALAPPDATA"]) / "Deckle" / "benchmark" / "runs" / "voxtral-transformers-validation-0003"

rows = [json.loads(l) for l in open(RUN / "results.jsonl", encoding="utf-8")]

print(f"Total rows : {len(rows)}\n")

# Inspect d8035ca0 and a66c3e92 specifically
for prefix in ("d8035ca0", "a66c3e92"):
    print(f"=== {prefix} ===")
    for r in rows:
        if not r["audio_id"].startswith(prefix):
            continue
        print(f"  regime={r['regime']:<16s} ok={r['ok']}")
        print(f"    ref gemini  : {r['reference_text_gemini']!r}")
        print(f"    text        : {r['text']!r}")
        print(f"    error       : {r.get('error','')!r}")
        print(f"    wer         : {r['metrics']['wer']}")
        print(f"    word_count  : {r['metrics']['word_count']}")
        print(f"    ref_words   : {r['metrics']['ref_word_count']}")
        j = r.get("judge")
        if j:
            print(f"    judge       : {j.get('axes')}  verdict={j.get('verdict','')[:100]!r}")
        print()
    print()

# Inspect chat regimes on 9bbfc858 and 7c2a983c for direct text comparison
print("=== Chat regimes on 9bbfc858 (11.3s, short) ===")
for r in rows:
    if not r["audio_id"].startswith("9bbfc858"):
        continue
    print(f"  --- {r['regime']:<16s}")
    print(f"      out  : {r['text'][:200]!r}")
    if r.get("judge"):
        print(f"      judge: {r['judge']['axes']}")
print()

print("=== Chat regimes on 7c2a983c (13.4s, short) ===")
for r in rows:
    if not r["audio_id"].startswith("7c2a983c"):
        continue
    print(f"  --- {r['regime']:<16s}")
    print(f"      out  : {r['text'][:200]!r}")
    if r.get("judge"):
        print(f"      judge: {r['judge']['axes']}")
