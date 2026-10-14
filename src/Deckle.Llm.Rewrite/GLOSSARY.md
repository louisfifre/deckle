---
name: glossary-deckle-llm-rewrite
description: "Rewrite service vocabulary, the single service every rewrite goes through, whoever asks. Read before touching rewrite plumbing or its inference engine. The Correction / Rewrite boundary itself lives in the root GLOSSARY.md."
---

# Deckle.Llm.Rewrite, Glossary

This module hosts the rewrite side of the Correction / Rewrite boundary defined in the root `GLOSSARY.md`. Read that boundary first, because it decides what may act silently.

## The service

**Rewrite service** :
The single service that carries every rewrite, whoever asks for it, such as transcription finalization, the paragraph rewrite or the escalations of the sentence stage. It holds the one profile store and the one home for the prompts. The inference engine sits behind it, so the engine can change without its clients knowing.
_Avoid_ : LlmService (an implementation name), Ollama (the current engine, not the service).

**LlmService** :
An earlier implementation name, built on the generic idea of a language-model service. The concept it carried is the rewrite service.

**Ollama** :
A local runtime that serves language models. It is the engine behind the rewrite service today, not the service itself.
