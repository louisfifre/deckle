---
name: context-deckle
description: "System-wide Deckle vocabulary — terms that classify across modules and belong to none, today the Correction / Rewrite boundary. Module vocabularies live next to their code; see CONTEXT-MAP.md."
type: agent-instructions
---

# Deckle — Context

System-wide glossary. This file holds only the language that classifies *across* modules — a term owned by one module lives in that module's `CONTEXT.md`, indexed in [CONTEXT-MAP.md](CONTEXT-MAP.md).

## Text operations — correction vs rewrite

Two families of automated text change, told apart by the nature of the change — not by the surface it acts on (voice dictation or typed keyboard, which are orthogonal) nor by the engine behind it. The family decides the risk, and therefore whether the change is allowed to act silently. The perimeter of the *applied edit* is what classifies: a generative model may act as judge among bounded candidates and the change remains a Correction; the moment the applied output is free regeneration, it is a Rewrite — silence is never allowed, whatever computed it.

**Correction** :
A bounded, in-place edit drawn from a closed set of possible changes — restoring a missing diacritic, dropping a hesitation, fixing punctuation or casing. It repairs what was typed or said and cannot introduce content that was not there, so it carries no meaning-drift risk: it may apply itself silently and is taken back through the correction inlay (see *Correction undo* in `src/Deckle.Autocorrect/CONTEXT.md`). Today: machine-wide diacritics restoration.
_Avoid_ : rewrite (which regenerates text — correction only repairs a span), autocorrect (the product/module name, not the operation itself).

**Rewrite** :
A generative regeneration of a span — a sentence or a paragraph — into new text: removing disfluencies and recomposing, restructuring into paragraphs, regrouping by theme. Because it rewrites the wording, it can drift from the original meaning, so it is offered after the fact (suggested or confirmed) rather than applied silently — until trust is earned. The same operation is meant to serve both finalized dictation and typed text. Every rewrite goes through the rewrite service (`src/Deckle.Llm.Rewrite/CONTEXT.md`).
_Avoid_ : correction (a bounded repair, not new text), reformulation (Rewrite is the Deckle term).
