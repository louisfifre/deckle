---
description: ONNX Runtime CPU inference behind a plain-array Run.
type: agent-instructions
---

# AGENTS.md — Deckle.Inference.Onnx

- Sessions stay on the CPU execution provider, to keep these small models off the GPU that Whisper holds.
- Deckle still references the `.DirectML` package: ONNX Runtime is one native DLL per process, and the GenAI sentence judge needs that build.
- Never append the DML provider in this module. Never pull `.Gpu` or GenAI APIs here.
