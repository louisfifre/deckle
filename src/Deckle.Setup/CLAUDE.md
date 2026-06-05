---
description: First-run wizard provisioning ASR runtimes and models — owns the flow, delegates provisioning to the backend modules.
type: agent-instructions
---

# CLAUDE.md — Deckle.Setup

The first-run wizard. `SetupWindow` owns the wizard state (`SetupContext`) and the three-page flow (Choices → Installing → Summary); it does **not** own provisioning. The primitives that actually download and place artifacts (`NativeRuntime`, `SpeechModels`, `Downloader`) live on the backend side (`Deckle.Transcription` / `Deckle.Transcription.Whisper`), beside the `IAsrBackend` they serve. The wizard orchestrates them and never reaches into those modules' internals. When a second ASR backend ships, it carries its own provisioning primitives and the wizard selects the set for the chosen backend.

Blocking at first launch — no model means nothing useful runs — and reopenable from Settings afterwards for a model swap or a native re-import.

`SetupContext` lives in this module, not in the backend: once the Whisper catalogs moved into `Deckle.Transcription.Whisper`, hosting the context in the parent created a parent↔child cycle. Only the wizard pages consume it, so it belongs here — moving it back into the Transcription parent reintroduces the cycle.
