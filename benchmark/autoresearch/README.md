---
name: readme-autoresearch
description: "Generic autoresearch workspace for measurable iterative generation, editing, judging, and keep/discard loops."
module: benchmark/autoresearch
---

# `autoresearch/` — iterative experiment loops

This folder is for the reusable shape of autoresearch, not for a specific candidate. The `autoresearch` skill names the contract: define a goal, define an exact metric command and extraction, bound the editable scope, establish a baseline, then run experiments that are committed, measured, kept or discarded.

Typical Deckle uses:

- rewrite-prompt tuning: generate a rewrite, score it against criteria, adjust the prompt, rerun;
- skill tuning: edit a skill, run a fixture task, judge the artifact, keep or discard;
- diagram/image generation: generate an artifact, inspect it with a visual or multimodal judge, iterate against explicit criteria;
- performance work: edit code, run a benchmark, compare the metric.

Put domain-specific inputs in subfolders named by the thing being optimized (`rewrite-prompts/`, `diagram-skills/`, `site-performance/`, ...).

## Layout

| Folder | Responsibility |
|---|---|
| [`campaigns/`](campaigns/) | One folder per measurable optimization loop. |
| `prompts/` | Prompt templates that drive generic rewrite or judging loops, and rewrite candidates owned by autoresearch campaigns. |
| `metrics/` | Metric extractors or wrappers when the measurement belongs to the loop itself: score extraction, comparison, thresholding, aggregation. |
| `judges/` | Domain-neutral judge wrappers or rubrics that decide whether a generated candidate satisfies explicit criteria. |
| `runners/` | Small orchestration helpers for candidate generation, metric execution, baseline comparison and keep/discard logging. Keep them generic. |

Use [`../lib/`](../lib/) for shared benchmark plumbing.
