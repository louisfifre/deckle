---
description: Input benchmark workspace — raw capture analysis, derived corpora, and behavior-level replay.
type: agent-instructions
---

# AGENTS.md — `benchmark/input/`

This workspace turns locally owned input telemetry into reproducible behavior targets.

Raw captures remain outside Git under the Deckle data root. Committed artifacts may contain schemas, analyzers, aggregate statistics, or small deliberately curated replay fixtures, but never a bulk copy of personal telemetry.

Keep three layers distinct:

- capture decoding reconstructs what the device reported;
- analysis derives kinematics and intent without changing runtime behavior;
- replay fixtures encode named behavioral contracts for production tests.

Do not tune a runtime constant directly from one trace. Establish the distribution, choose a behavior target, then preserve that target in a replay test.
