---
name: readme-benchmark
description: "Index for Deckle's remaining benchmark workspace: the rewrite evaluations, kept beside the app but outside its release discipline."
module: benchmark
---

# `benchmark/` — Deckle benchmark workspace

Experimental harnesses kept near Deckle but not product code. Only the rewrite evaluations remain; the other benches were removed and what they concluded is in [`docs/research/benchmark-conclusions.md`](../docs/research/benchmark-conclusions.md).

| Folder | Responsibility |
|---|---|
| [`rewrite/paragraph-gate`](rewrite/paragraph-gate/) | Runs the production paragraph prompt, engine and diff gate on a local model. |
| [`rewrite/model-compare`](rewrite/model-compare/) | Runs Claude models × prompt variants on the whole-dictation rewrite, judged by an LLM, and optimizes a profile's prompt in campaigns. |
| [`capture-tail`](capture-tail/) | Reads the raw takes of the dictation corpus and measures, with aggregate output only (never text or file names), what a shorter or longer end-of-capture tail would change: sound left outside (`energy`) and final words changed when Whisper is replayed on the cut audio (`replay`). |

`Directory.Build.props` and `Directory.Packages.props` here cut the bench projects off from the root build conventions and from Central Package Management, so each study keeps its own package versions.
