---
description: Autoresearch benchmark workspace — reusable iterative optimization loops.
type: agent-instructions
---

# AGENTS.md — `benchmark/autoresearch/`

This folder is for generic autoresearch loops, not for a model-family study.
Follow `D:\skills\global\autoresearch\SKILL.md` when creating or running a
campaign:

- define the goal and the exact measurable metric;
- define the command, extraction rule, direction, and baseline;
- bound the editable scope and time/iteration budget;
- run experiments one at a time, keeping only measured improvements;
- log the decision trail so another campaign can copy the pattern.

If the artifact being evaluated is ASR-specific, keep its corpus, prompts,
sources, judges, and metrics under `benchmark/asr/`. Use this folder only when
the reusable object is the autoresearch loop itself.
