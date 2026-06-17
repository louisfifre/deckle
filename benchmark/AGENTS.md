---
description: Benchmark workspace router — choose the right benchmark family before touching files.
type: agent-instructions
---

# AGENTS.md — `benchmark/`

`benchmark/` is a host folder, not one benchmark. Pick the child workspace by
responsibility before editing:

- `asr/` — speech-recognition and speech-output evaluation: corpora, ASR/TTS
  sources, prompt regimes, LLM judges, WER/looping/leak metrics, and frozen
  Voxtral/Phi/TTS studies.
- `autoresearch/` — generic autonomous experiment loops following the
  autoresearch skill: goal, metric command/extraction, scope, baseline,
  experiment commit, run, measure, keep/discard, log.
- `lib/` — cross-benchmark infrastructure only: path resolution, environment
  loading, event logs, resource monitoring. No ASR semantics here.
- `viewers/` — generic result viewers. Keep them independent of ASR-specific
  source, judge, or metric contracts.

Do not confuse an ASR study with autoresearch. Voxtral validation is an ASR
study; it only becomes autoresearch when the active object is the iterative
search protocol itself.
