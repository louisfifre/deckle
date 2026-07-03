---
name: readme-autoresearch
description: "Generic autoresearch workspace for measurable iterative generation, editing, judging, and keep/discard loops."
type: module-readme
module: benchmark/autoresearch
---

# `autoresearch/` — iterative experiment loops

This folder is for the reusable shape of autoresearch, not for a specific ASR candidate. The `autoresearch` skill names the contract: define a goal, define an exact metric command and extraction, bound the editable scope, establish a baseline, then run experiments that are committed, measured, kept or discarded.

Typical Deckle uses:

- rewrite-prompt tuning: generate a rewrite, score it against criteria, adjust the prompt, rerun;
- skill tuning: edit a skill, run a fixture task, judge the artifact, keep or discard;
- diagram/image generation: generate an artifact, inspect it with a visual or multimodal judge, iterate against explicit criteria;
- performance work: edit code, run a benchmark, compare the metric.

Put domain-specific inputs in subfolders named by the thing being optimized (`rewrite-prompts/`, `diagram-skills/`, `site-performance/`, ...). Keep ASR model studies under [`../asr/studies/`](../asr/studies/) unless the primary artifact is the autoresearch loop itself.

## Layout

| Folder | Responsibility |
|---|---|
| [`campaigns/`](campaigns/) | One folder per measurable optimization loop. |
| [`prompts/`](prompts/) | Prompt templates and rewrite candidates owned by autoresearch campaigns. |
| [`metrics/`](metrics/) | Metric extractors or wrappers when the measurement is generic to the loop. |
| [`judges/`](judges/) | Domain-neutral judge wrappers or rubrics for iterative scoring. |
| [`runners/`](runners/) | Small orchestration helpers for running candidates, logging results, and comparing baselines. |

Use [`../lib/`](../lib/) for shared benchmark plumbing. Keep ASR-specific corpora, transcription judges, WER metrics, and Voxtral/Whisper prompts in [`../asr/`](../asr/).
