---
name: context-deckle-llm-rewrite
description: "Rewrite service vocabulary — the single service every rewrite goes through, whoever asks. Read before touching rewrite plumbing or its inference engine. The Correction / Rewrite boundary itself lives in the root CONTEXT.md."
type: agent-instructions
---

# Deckle.Llm.Rewrite — Context

This module hosts the rewrite side of the Correction / Rewrite boundary defined in the root `CONTEXT.md` — read that boundary first; it decides what may act silently.

## The service

**Rewrite service** :
The single service every rewrite goes through, whoever asks — transcription finalization, the paragraph rewrite, the sentence stage's escalations. One profile store, one home for the prompts; the inference engine sits behind its seam and can change without the clients knowing (decided target: in-process ONNX; Ollama until that migration). Also the natural place to serialize local heavy compute — its consumers share one GPU.
_Avoid_ : LlmService (an implementation name), Ollama (the current engine, not the service).
