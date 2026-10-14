---
name: glossary-map-deckle
description: "Index of Deckle's bounded-context glossaries — where each module's GLOSSARY.md lives and the relationships that carry meaning. Read to find which vocabulary governs the code at hand."
---

# Glossary Map — Deckle

The nearest `GLOSSARY.md` up the tree governs the code you're touching. This map indexes them.

## Glossaries

- [Settings](src/Deckle.Settings/GLOSSARY.md) — the exposable and its kinds, of which a setting is one
- [Scripts](scripts/GLOSSARY.md) — terminal-launcher navigation, actions, executions, tracking, journals, and results
- [Tests](tests/GLOSSARY.md) — the test taxonomy: four automatic categories, three outside the automatic scope
- [Diagnostics](src/Deckle.Diagnostics/GLOSSARY.md) — observability: admission vs view, the controls, filters, logs and datasets that quiet or split the stream (covers `.Logging` and `.Telemetry`)
- [Security](src/Deckle.Security/GLOSSARY.md) — trusted-session boundary, captured content, and custody of persisted copies
- [Transcription](src/Deckle.Transcription/GLOSSARY.md) — entry points, T1 fidelity criteria, segmentation units
- [Audio](src/Deckle.Audio/GLOSSARY.md) — display level vs transcription pre-processing
- [Input](src/Deckle.Input/GLOSSARY.md) — the contact frame and the report → frame → intention chain
- [Input.Trackpad](src/Deckle.Input.Trackpad/GLOSSARY.md) — the recognizer and the three-finger drag
- [Autocorrect](src/Deckle.Autocorrect/GLOSSARY.md) — correctable surfaces, the two stages, datasets, learning
- [Llm.Rewrite](src/Deckle.Llm.Rewrite/GLOSSARY.md) — the Correction / Rewrite boundary, the rewrite and the rewrite service
- [Anytype](src/Deckle.Anytype/GLOSSARY.md) — backend / host / surfaces, bot vs token, Home types (covers `.Mcp`)
- [Home](src/Deckle.Home/GLOSSARY.md) — public home-inventory norm vs personal room registry and inventory
- [Hud Chrono](src/Deckle.Hud/Chrono/GLOSSARY.md) — the chrono face elements and their states

## Relationships

- **Audio → Transcription**: Audio captures and pre-processes the buffer; Transcription cuts it into utterances and decodes them
- **Vad ← Transcription**: the neural VAD (`Deckle.Vad`) trims silence for Transcription's monolithic path; the energy segmenter is Transcription's own live device — the two must not be conflated
- **Input → Input.Trackpad**: Input assembles contact frames; the Trackpad recognizer consumes them into gesture intentions
- **Autocorrect ↔ Llm.Rewrite**: both sit on the Correction / Rewrite boundary, drawn in Llm.Rewrite — Autocorrect owns bounded corrections, the rewrite service carries every free regeneration (including the sentence stage's escalations)
- **Diagnostics → everything**: the observability vocabulary governs every module's log stream; the `observability` test category (Tests) exercises it
- **Security → everything**: Security classifies captured content and its custody boundary wherever a module stores, exports, or transmits it
- **Home → Anytype**: Home owns the inventory language and guards; Anytype provides the local storage and schema coordinates
