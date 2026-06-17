---
name: readme-study-phibench
description: "Suspended C# bench for Phi-4 multimodal audio via ONNX Runtime GenAI (OGA). Blocked upstream — OGA doesn't inject audio embeddings. Findings in the benchmark JOURNAL."
type: study
module: benchmark/asr/studies/PhiBench
---

# `studies/PhiBench/` — Phi-4 multimodal via OGA (C#)

A C# (.NET 10) bench that drove Phi-4 multimodal through **ONNX Runtime GenAI (OGA)** with the DirectML provider, to test it as an ASR backend on the private corpus.

> **Status — suspended, blocked upstream.** Phi-4-multimodal via OGA returns refusals ("I can't transcribe…") on every prompt variant — it sees no audio. Upstream bug [onnxruntime-genai#1455](https://github.com/microsoft/onnxruntime-genai/issues/1455) (open): OGA doesn't inject the audio embeddings into the LM, while the transformers/torch build of the same model works. Nothing Deckle can patch trivially. Findings in [`../../JOURNAL.md`](../../JOURNAL.md) (2026-05-27).

Build with `dotnet build` (Debug, x64); the `Directory.Build.props` at the benchmark root still applies via the MSBuild upward walk. Kept as a worked reference for the C# OGA + DirectML path, should the upstream fix land.
