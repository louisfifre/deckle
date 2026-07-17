---
name: context-map-deckle
description: "Index of Deckle's bounded-context glossaries — where each module's CONTEXT.md lives and the relationships that carry meaning. Read to find which vocabulary governs the code at hand."
type: agent-instructions
---

# Context Map — Deckle

The nearest `CONTEXT.md` up the tree governs the code you're touching. This map indexes them.

## Contexts

- [System-wide](CONTEXT.md) — language that classifies across modules; today the Correction / Rewrite boundary
- [Tests](tests/CONTEXT.md) — the test taxonomy: four automatic categories, two manual ones
- [Diagnostics](src/Deckle.Diagnostics/CONTEXT.md) — observability: admission vs view, the five quieting controls (covers `.Logging` and `.Telemetry`)
- [Transcription](src/Deckle.Transcription/CONTEXT.md) — entry points, T1 fidelity criteria, segmentation units
- [Audio](src/Deckle.Audio/CONTEXT.md) — display level vs transcription pre-processing
- [Input](src/Deckle.Input/CONTEXT.md) — the contact frame and the report → frame → intention chain
- [Input.Trackpad](src/Deckle.Input.Trackpad/CONTEXT.md) — the recognizer and the three-finger drag
- [Autocorrect](src/Deckle.Autocorrect/CONTEXT.md) — correctable surfaces, the two stages, datasets, learning
- [Llm.Rewrite](src/Deckle.Llm.Rewrite/CONTEXT.md) — the rewrite service
- [Anytype](src/Deckle.Anytype/CONTEXT.md) — backend / host / surfaces, bot vs token, Home types (covers `.Mcp`)
- [Hud Chrono](src/Deckle.Hud/Chrono/CONTEXT.md) — the chrono face elements and their states

## Relationships

- **Audio → Transcription**: Audio captures and pre-processes the buffer; Transcription cuts it into utterances and decodes them
- **Vad ← Transcription**: the neural VAD (`Deckle.Vad`) trims silence for Transcription's monolithic path; the energy segmenter is Transcription's own live device — the two must not be conflated
- **Input → Input.Trackpad**: Input assembles contact frames; the Trackpad recognizer consumes them into gesture intentions
- **Autocorrect ↔ Llm.Rewrite**: both sit on the system-wide Correction / Rewrite boundary — Autocorrect owns bounded corrections, the rewrite service carries every free regeneration (including the sentence stage's escalations)
- **Diagnostics → everything**: the observability vocabulary governs every module's log stream; the `observability` test category (Tests) exercises it
