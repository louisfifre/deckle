---
name: adr-0005-pluggable-asr-backend-via-iasrbackend
description: "Records the split of transcription into a backend-agnostic Deckle.Transcription orchestrator behind an IAsrBackend contract, with each ASR engine in its own child module (Deckle.Transcription.Whisper). Read before adding an ASR backend or touching the engine/backend boundary."
type: adr
---

# ADR-0005 — Pluggable ASR backend via IAsrBackend

**Status** — accepted 2026-05-23

## Context

Speech transcription in Deckle ran its first year exclusively on whisper.cpp. The `WhispEngine` (2 158 lines) carried three entangled responsibilities at once: business orchestration (hotkey state machine, audio-capture coordination, LLM-rewrite trigger, UIA paste), pipeline mechanics (native callbacks, whisper.cpp log parsing, repetition detection), and the P/Invoke into `libwhisper.dll`.

The likely arrival of an alternative ASR engine forces clarifying the boundary: comparing two engines in real use requires switching between them without duplicating the whole pipeline. And `WhispEngine.cs` is far past the ~500-line modularity vigilance threshold — `deckle-modularite` requires examining responsibility when a file becomes uncomfortable.

## Options considered

- **A. One module, one engine, branch the alternative by a flag in `WhispEngine`.** Maximum code conservation. But the already-monolithic file doubles, the P/Invoke and an alternative client coexist in one file, and every future addition needs a new branch. No usable boundary.
- **B. Two parallel engines, the app chooses which to instantiate.** Separates the implementations, but all the orchestration (state machine, capture, rewrite, paste, telemetry) is duplicated; every pipeline change must propagate to both.
- **C. A single orchestrator in `Deckle.Transcription`, an `IAsrBackend` contract, implementations in child modules.** The parent carries the backend-agnostic orchestration. A narrow interface (`LoadModelAsync`, `UnloadModel`, `TranscribeAsync`, `Dispose` + three properties) captures what a backend must provide. Each backend lives in its own child (`Deckle.Transcription.Whisper`, later others). The host app injects the chosen backend into the orchestrator. Conforms to the parent/children pattern of ADR-0004.

## Decision

Option C. Transcription is organized as a parent plus one child module per backend. The parent `Deckle.Transcription` carries the `TranscriptionEngine` orchestrator (formerly `WhispEngine`), the `IAsrBackend` contract, the DTOs (`TranscriptionResult`, `TranscriptionSegment`, `ModelLoadResult`), the `TranscriptionSettings` POCO, the Settings UI, the `ITranscriptionEngineHost` bridge, and the `DeckleWhispSource` EventSource provider. The child `Deckle.Transcription.Whisper` carries `WhisperBackend` implementing `IAsrBackend`, plus all the native machinery (P/Invoke, structs, callbacks, whisper.cpp log parsing, the `SpeechModels` and `NativeRuntime` catalogues).

The `Backend` suffix is not in the `deckle-nomenclature` closed vocabulary; this ADR extends it. The responsibility — "interchangeable ASR-inference implementation (model load, inference, release)" — is nameable in one phrase, and "backend" is the established idiom in the ML/AI world (llama.cpp, whisper.cpp, transformers all speak of backends). `IAsrService` and `ISpeechRecognizer` were rejected: `Service` is too generic, `Recognizer` fails to capture the pluggable/swappable dimension that justifies the whole effort.

## Consequences

Easier: adding a second backend is a new child module implementing `IAsrBackend`; the orchestrator, UI, settings and app bridge do not move. The split forces separation of concerns — the orchestrator never touches P/Invoke, the backend knows nothing of UIA paste or LLM rewrite. `TranscriptionEngine.cs` drops below ~1 800 lines (from 2 158); the ~500 whisper-specific lines live in the child.

Harder: two modules to maintain plus the interface ceremony (`IAsrBackend` must stay stable). A real administrative overhead, offset by graph readability and by the future ability to pivot backend without touching the rest.

Impossible: a consumer of the orchestrator calling whisper.cpp P/Invoke directly — it must go through the backend. The bridge prevents circular app↔engine coupling. The child cannot be referenced by the parent; any change that would require the parent to know a backend-specific type signals a bad design to revisit.
