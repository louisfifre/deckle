---
name: readme-bench-voxtral-poc
description: "Bench scenario evaluating Voxtral Mini 3B as a Whisper alternative in the Deckle transcription pipeline. Read before running, modifying, or extending this bench, or before extracting its results."
type: module-readme
module: benchmark/benches/voxtral-poc
---

# bench voxtral-poc

POC bench evaluating Voxtral Mini 3B as a Whisper alternative inside the Deckle transcription pipeline.

## What

For each sample of the `corpora/voxtral-poc/` corpus and each prompt regime (`V1_raw` through `V5_traduit_en`), the bench:

1. Transcribes via the chosen source (default: Voxtral DML).
2. Computes WER + CER against the corpus Whisper reference.
3. Computes objective metrics (looping, hallucinations).
4. Asks Claude Haiku to score on 4 axes (faithfulness, cleanness, hallucinations, regime compliance) plus a `whisper_ref_suspecte` flag.
5. Writes one JSON line per experiment into `runs/<run-id>/results.jsonl`.

## Why

- Phase 1/2 (llama.cpp + Jinja chat template) produced catastrophic results because the stack pushed Voxtral into conversational chat mode — it paraphrased instead of transcribing.
- Phase 3 (Transformers + `apply_transcription_request`) fixed that on a smoke test. This bench generalizes across the full corpus × 5 regimes to actually quantify it.

## How

```pwsh
# 1. Have a .env at the benchmark/ root with ANTHROPIC_API_KEY
#    (see benchmark/.env.example).

# 2. Have a voxtral-poc corpus under corpora/voxtral-poc/ with:
#    - corpus.jsonl (Deckle telemetry payload lines)
#    - *.wav (referenced in corpus.jsonl payload.audio_file)
#    Corpora are not versioned — each user brings their own.

# 3. Have the .venv-voxtral-dml venv ready (see benchmark/CLAUDE.md).

# 4. Run:
.venv-voxtral-dml\Scripts\python.exe benches\voxtral-poc\bench.py

# Variants:
.venv-voxtral-dml\Scripts\python.exe benches\voxtral-poc\bench.py --limit 3
.venv-voxtral-dml\Scripts\python.exe benches\voxtral-poc\bench.py --regimes V1_raw
.venv-voxtral-dml\Scripts\python.exe benches\voxtral-poc\bench.py --skip-judge
.venv-voxtral-dml\Scripts\python.exe benches\voxtral-poc\bench.py --source whisper-cpp
```

## Output

`runs/voxtral-poc-<timestamp>/results.jsonl` — one line per experiment, stable schema:

```json
{
  "audio_id":         "bc08abb2...",
  "audio_seconds":    12.88,
  "tier":             "very-short",
  "reference_text":   "Ça m'a l'air bon...",
  "source":           "voxtral-dml",
  "regime":           "V1_raw",
  "regime_prompt":    "Transcris cet audio en français.",
  "ok":               true,
  "text":             "Ça m'a l'air bon. Je vais voir...",
  "elapsed_s":        2.72,
  "rtf":              0.21,
  "generated_tokens": 38,
  "extras":           { "prep_s": 0.01, "gen_s": 2.7, "tok_per_s": 14, ... },
  "metrics":          { "wer": 0.0, "cer": 0.0, "looping_score": 0.0, ... },
  "judge":            { "axes": {...}, "verdict": "...", "parse_ok": true, ... }
}
```

## Current limitations

- Sources: `voxtral-dml` validated, `whisper-cpp` wired but not retested in v2 (validate on a sample).
- Judge: per-row Haiku only. The macro Opus mode (Haiku curation → Opus summary) lives in `lib/judges/claude.py` but is not wired here yet.
- No automatic fp16 → fp32 fallback. If DML complains, relaunch with `--dtype float32`.
