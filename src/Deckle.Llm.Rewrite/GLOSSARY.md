---
name: glossary-deckle-llm-rewrite
description: "Rewrite vocabulary. It draws the Correction / Rewrite boundary, which decides what may act silently, defines the rewrite itself and the rewrite service, the single service every rewrite goes through, whoever asks. Read before touching rewrite plumbing or its inference engine."
---

# Deckle.Llm.Rewrite, Glossary

This module hosts the rewrite side of the Correction / Rewrite boundary and the boundary itself. The correction side is defined in `src/Deckle.Autocorrect/GLOSSARY.md`. Read the boundary first, because it decides what may act silently.

## Text operations, correction vs rewrite

Two families of automated text change, told apart by the nature of the change. The surface it acts on does not decide, whether dictated or typed, and neither does the engine behind it. The family sets the risk, and the risk sets whether the change may act silently. What classifies is the edit actually applied. A generative model may judge between bounded candidates and the change stays a Correction. When the applied text is freely regenerated, it is a Rewrite, and silence is never allowed, whatever produced it.

**Rewrite** :
A generative text change that regenerates a span, a sentence or a paragraph, into new text, for example to remove disfluencies, split into paragraphs or regroup by theme. Because it changes the wording, it can drift from the original meaning. It is therefore offered after the fact, suggested or confirmed, never applied silently. The same operation is meant to serve finalized dictation and typed text alike. The rewrite service below carries every rewrite.
_Avoid_ : correction (a bounded repair, not new text), reformulation (Rewrite is the Deckle term).

**Correction** :
A bounded text change that repairs a span in place, defined in `src/Deckle.Autocorrect/GLOSSARY.md`. It is the side of the boundary that may act silently.

**Reformulation** :
In ordinary use, saying the same thing in other words. Deckle calls a generated reformulation of a span a rewrite. The plausible reformulation of `src/Deckle.Transcription/GLOSSARY.md` is a different thing, a fidelity defect.

## The service

**Rewrite service** :
The single service that carries every rewrite, whoever asks for it, such as transcription finalization, the paragraph rewrite or the escalations of the sentence stage. It holds the one profile store and the one home for the prompts. The inference engine sits behind it, so the engine can change without its clients knowing.
_Avoid_ : LlmService (an implementation name), Ollama (the current engine, not the service).

**LlmService** :
An earlier implementation name, built on the generic idea of a language-model service. The concept it carried is the rewrite service.

**Ollama** :
A local runtime that serves language models. It is the engine behind the rewrite service today, not the service itself.
