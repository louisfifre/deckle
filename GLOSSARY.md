---
name: glossary-deckle
description: "System-wide Deckle vocabulary. It holds the terms that classify across modules and belong to none of them, today the Correction / Rewrite boundary. Module vocabularies live next to their code, indexed in GLOSSARY-MAP.md."
---

# Deckle, Glossary

System-wide glossary. This file holds only the language that classifies *across* modules. A term owned by one module lives in that module's `GLOSSARY.md`, indexed in [GLOSSARY-MAP.md](GLOSSARY-MAP.md).

## Text operations, correction vs rewrite

Two families of automated text change, told apart by the nature of the change. The surface it acts on does not decide, whether dictated or typed, and neither does the engine behind it. The family sets the risk, and the risk sets whether the change may act silently. What classifies is the edit actually applied. A generative model may judge between bounded candidates and the change stays a Correction. When the applied text is freely regenerated, it is a Rewrite, and silence is never allowed, whatever produced it.

**Correction** :
A bounded text change that repairs a span in place, drawn from a closed set of possible edits, such as restoring a diacritic, dropping a hesitation or fixing casing. It cannot add content that was not there, so it may act silently and is taken back through the correction undo, defined in `src/Deckle.Autocorrect/GLOSSARY.md`.
_Avoid_ : rewrite (regenerates text, a correction only repairs), autocorrect (the module name, not the operation).

**Rewrite** :
A generative text change that regenerates a span, a sentence or a paragraph, into new text, for example to remove disfluencies, split into paragraphs or regroup by theme. Because it changes the wording, it can drift from the original meaning. It is therefore offered after the fact, suggested or confirmed, rather than applied silently, until trust is earned. The same operation is meant to serve finalized dictation and typed text alike. The rewrite service, defined in `src/Deckle.Llm.Rewrite/GLOSSARY.md`, carries every rewrite.
_Avoid_ : correction (a bounded repair, not new text), reformulation (Rewrite is the Deckle term).

**Autocorrect** :
In ordinary use, a feature that fixes typing errors on its own. In Deckle it is the name of the module and of the product feature, while the operation they perform is a correction.

**Reformulation** :
In ordinary use, saying the same thing in other words. Deckle calls a generated reformulation of a span a rewrite. The plausible reformulation of `src/Deckle.Transcription/GLOSSARY.md` is a different thing, a fidelity defect.
