---
description: First-run wizard, installer and updater; delegates provisioning.
type: agent-instructions
---

# AGENTS.md — Deckle.Setup

## Wizard

- `SetupWindow` owns the wizard state and the flow. It does not own provisioning.
- Module wording is keyed by module id in this module's `.resw`, mirrored to the root map. The rule and its why are in `Deckle.Modules/AGENTS.md`.
- The install step is a plan, not a hardcoded sequence. `InstallPlan` maps the selected modules to install items; it only selects and composes provisioning gestures.
- Each backend owns its whole provisioning gesture beside the runtime it serves: `Deckle.Transcription.Whisper`, `Deckle.Vad`, `Deckle.Autocorrect.Mlm`, `Deckle.Anytype`. A new ASR backend brings its own gesture.
- Autocorrect installs the CamemBERT set by decision, not as an option.
- Anytype MCP adapters are not install items: the application payload already carries them.
- The wizard blocks at first launch, because without a model nothing useful runs. It can be reopened from Settings afterwards.
- `SetupContext` lives here, not in the Transcription parent. Since the Whisper catalogs moved into `Deckle.Transcription.Whisper`, hosting it in the parent creates a parent↔child cycle.

## Install and update modes

- Install mode exists because `AppPaths` freezes the data root at first touch. The temp process launched by the stub can place and integrate. Only the installed process, launched with `DECKLE_DATA_ROOT` in its environment, can provision into a custom root. Both branches live in `src/Deckle.App/App.Install.cs`, not here.
- Update mode mirrors the install split with the roles reversed. The new payload's temp process runs `DeployPage` verbatim over the live install, then hands over to the installed process.
- `UpdateService` runs only when the Settings opt-out allows it and the launch is an installed one. Dev builds never see updates.
