---
description: ASR benchmark workspace — sources, judges, corpora, speech studies.
type: agent-instructions
---

# AGENTS.md — benchmark/asr

## Non-negotiables

- **Reuse by concept.** A new backend is a new file under `lib/sources/` implementing the existing contract, not a fork of a bench.
- **Privacy.** Corpora hold user audio and are never versioned. Each machine brings its own samples, extracted from Deckle telemetry or captured fresh.
- **Code vs data.** Code is versioned; data (corpora, runs) lives outside the worktree under `%LOCALAPPDATA%`, resolved by `../lib/paths.py`.

## No Q4 for ASR/TTS

Use BF16 or FP16 for speech models. Never quantize them to 4-bit (Q4_K_M, Q4F16, INT4) for Deckle. On the private French corpus, Q4 loses fidelity that automatic metrics barely register but a reader feels: pronoun and suffix slips, occasional hallucinations, far higher WER variance. Treat 4-bit as a smoke-test convenience, never a shipping candidate.
