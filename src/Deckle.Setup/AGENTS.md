---
description: First-run wizard — module selection then ASR provisioning; owns the flow, delegates provisioning to the backend modules.
type: agent-instructions
---

# AGENTS.md — Deckle.Setup

The first-run wizard. `SetupWindow` owns the wizard state (`SetupContext`) and the four-page flow (Modules → Choices → Installing → Summary); it does **not** own provisioning. `ModulesPage` is the presence selector: it renders the `Deckle.Modules` catalogue as checkbox cards, cascades on the dependency edges through `ModuleGraph`, records the choice via `ModulePresence`, and routes into the Choices page only when Dictation is selected — otherwise straight to the install step when something is missing, or to completion. Module wording is keyed by module id in this module's `.resw` (mirrored to the root map): the future companion must be able to name a module whose DLLs are not on disk, so the wording cannot live in the described assembly.

The install step is a plan, not a hardcoded sequence: `InstallPlan` maps the selected modules to `InstallItem`s (Dictation → native runtime + chosen model + Silero VAD; Autocorrect → the CamemBERT set, in by decision, not an option; Anytype → the pinned anytype-cli binary — the bot-account auth is a later, interactive act) and `InstallingPage` renders one row per item and runs them sequentially. The provisioning primitives stay in the modules they serve (`NativeRuntime`/`SpeechModels` in `Deckle.Transcription.Whisper`, `SileroVadModel` in `Deckle.Vad`, `CamembertAssets` in `Deckle.Autocorrect.Mlm`, `BackendInstallation` in `Deckle.Anytype`); the plan only composes them. When a second ASR backend ships, it carries its own provisioning primitives and the plan selects the set for the chosen backend. The primitives that actually download and place artifacts (`NativeRuntime`, `SpeechModels`, `Downloader`) live on the backend side (`Deckle.Transcription` / `Deckle.Transcription.Whisper`), beside the `IAsrBackend` they serve. The wizard orchestrates them and never reaches into those modules' internals. When a second ASR backend ships, it carries its own provisioning primitives and the wizard selects the set for the chosen backend.

Blocking at first launch — no model means nothing useful runs — and reopenable from Settings afterwards for a model swap or a native re-import.

`SetupContext` lives in this module, not in the backend: once the Whisper catalogs moved into `Deckle.Transcription.Whisper`, hosting the context in the parent created a parent↔child cycle. Only the wizard pages consume it, so it belongs here — moving it back into the Transcription parent reintroduces the cycle.
