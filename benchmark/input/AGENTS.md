---
description: Input benchmark — capture analysis, derived corpora, replay.
type: agent-instructions
---

# AGENTS.md — `benchmark/input/`

- Raw captures stay outside Git, under the Deckle data root. Commit schemas, analyzers, aggregate statistics or small curated replay fixtures, never a bulk copy of personal telemetry.
- Raw calibration captures are disposable when the production input model changes. Delete the previous trackpad and wheel captures before recording the new baseline. Never mix pre-change and post-change data in one report.
- Keep three layers distinct: capture decoding reconstructs what the device reported; analysis derives kinematics and intent without changing runtime behavior; replay fixtures encode named behavioral contracts for production tests.
- Never tune a runtime constant from one trace. Establish the distribution, choose a behavior target, then preserve it in a replay test.
