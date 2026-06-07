---
description: ONNX Runtime CPU inference substrate — isolates the OnnxRuntime dependency behind a plain-array Run.
type: agent-instructions
---

# CLAUDE.md — Deckle.Inference.Onnx

Support module: runs ONNX models on the CPU execution provider, isolating the
`Microsoft.ML.OnnxRuntime` dependency from the rest of the app. Callers hand it a
model path and inputs and get outputs back in plain arrays (`OnnxModelSession`); it
owns no domain state. Dependencies point one way, toward it.

Plain `Microsoft.ML.OnnxRuntime` (CPU) by design — keep models off the GPU that
whisper holds. Never pull the `.DirectML` / `.Gpu` / genai variants here.
