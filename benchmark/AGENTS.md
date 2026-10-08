---
description: Benchmark workspace router — pick the right child workspace first.
type: agent-instructions
---

# AGENTS.md — benchmark

- `benchmark/` is a host folder, not one benchmark. Pick the child workspace by responsibility before editing.
- `lib/` is cross-benchmark infrastructure only. It carries no ASR semantics.
- `viewers/` stay independent of ASR-specific source, judge and metric contracts.
- Autoresearch loops follow the `autoresearch` skill, which lives outside this repository.
- An ASR study is not autoresearch. Voxtral validation is an ASR study; it becomes autoresearch only when the active object is the iterative search protocol itself.
