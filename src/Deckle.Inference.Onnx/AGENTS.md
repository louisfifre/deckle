---
description: ONNX Runtime CPU inference substrate — isolates the process-wide OnnxRuntime dependency behind a plain-array Run.
type: agent-instructions
---

# AGENTS.md — Deckle.Inference.Onnx

Support module: runs ONNX models on the CPU execution provider, isolating the `Microsoft.ML.OnnxRuntime` API from the rest of the app. Callers hand it a model path and inputs and get outputs back in plain arrays (`OnnxModelSession`); it owns no domain state. Dependencies point one way, toward it.

Sessions stay on the CPU execution provider by design — keep these small models off the GPU that whisper holds. Deckle nevertheless references the `.DirectML` package because ONNX Runtime is one native DLL per process and the GenAI sentence judge needs that build; do not append the DML provider in this module, and never pull `.Gpu` or GenAI APIs here.
