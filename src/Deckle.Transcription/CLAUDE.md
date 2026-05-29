---
name: claude-deckle-transcription
description: "Doctrine for Deckle.Transcription, the backend-agnostic transcription orchestrator (IAsrBackend, state machine, UIA paste, capture coordination). Read before touching the pipeline, the backend contract, or the paste path."
type: agent-instructions
module: Deckle.Transcription
---

# CLAUDE.md — Deckle.Transcription

Voice transcription orchestrator. Covers the whole pipeline from hotkey to final clipboard write: audio capture (delegated to `Deckle.Audio`), invocation of an ASR backend through the `IAsrBackend` interface, result filtering, optional LLM rewriting (delegated to `Deckle.Llm.Rewrite` for the rewriting engine and to `Deckle.Llm` for Ollama availability), clipboard write, optional paste. The module also owns its Settings UI (`WhisperPage.xaml`).

The module is **backend-agnostic**. The ASR implementation lives in a child module (`Deckle.Transcription.Whisper` today; `Deckle.Transcription.Voxtral` planned). The pattern mirrors the one established by `Deckle.Diagnostics` → `Deckle.Diagnostics.Logging` + `Deckle.Diagnostics.Telemetry`: the parent owns the contracts and the orchestration, the children own the specific implementations. The decision is recorded in [ADR 0010](../../docs/adr/0010-backend-asr-pluggable-via-iasrbackend.md).

The contract with the host app goes through `ITranscriptionEngineHost` — a bridge interface that exposes the settings useful on the engine side without coupling `Deckle.Transcription` to `Deckle.Settings`. The app implements `AppTranscriptionEngineHost` in `src/Deckle.App/Engine/`, and composes the engine with a concrete `IAsrBackend` (`WhisperBackend` today). Transcription is invoked through `_engine.RequestToggle(...)` from the hotkey handler.

## IAsrBackend contract

`IAsrBackend` is the interface every ASR backend implements. Four methods: `LoadModelAsync`, `UnloadModel`, `TranscribeAsync`, `Dispose`. Three properties: `Name` (stable identifier for telemetry), `IsModelLoaded`, `DetectedAccelerator` (backend-defined vocabulary: `CPU`, `Vulkan`, `CUDA`, `Metal`, etc.).

`TranscribeAsync(pcmSamples, segmentSink, ct)` takes a mono 16 kHz PCM buffer, a synchronous callback to stream segments as inference progresses (HUD/LogWindow subscribe through `TranscriptionEngine.NewSegment`), and a cancellation token. Returns a `TranscriptionResult` that aggregates the produced segments, the assembled text, and the phase-by-phase timings (pre-VAD init, VAD, total). The `Backend` suffix is not in the closed Deckle vocabulary but it is the established idiom in the ML/AI world (llama.cpp, whisper.cpp, transformers all talk about "backends") — vocabulary extension ratified by ADR 0010.

The orchestrator never touches P/Invoke, native callbacks, or C structs — that whole machinery lives in the backend. Consequence: adding a second backend (Voxtral via Python+Transformers, for example) is a new child module that implements `IAsrBackend`; the orchestrator stays unchanged, the app injects the right backend based on an `Engine = Whisper | Voxtral` setting.

## Monolithic transcription pipeline

The pipeline runs in a single `_backend.TranscribeAsync(...)` call that returns as soon as the backend is done. For Whisper today it is a synchronous wrapper around `whisper_full()`; for an HTTP backend (Voxtral), it would be a real `await`. No external chunking: the backend manages its internal window (30 s + dynamic seek in Whisper, equivalent on the Voxtral side), the VAD cuts silences upstream, and segments arrive as they come through the `segmentSink`.

`Record()` accumulates all captured audio into a single `List<byte>` and returns a `float[]` on Stop; `TranscribeAsync(float[])` makes a single awaited `_backend.TranscribeAsync(...)` call and the backend handles inter-window context propagation internally. The worker thread blocks only at its top-level rendezvous, so a future true async backend can keep its own I/O path asynchronous without the orchestrator immediately forcing `.GetResult()` inside the pipeline. The final assembled text lands in `TranscriptionResult.FullText`.

### Whisper initial prompt

Whisper is not instruction-tuned. The `initial_prompt` (`TranscriptionSettings.Engine.InitialPrompt` field, read by the Whisper backend) is a **stylistic sample to imitate**, not an instruction. Meta phrases ("here is a transcription", "with careful punctuation") are at best neutral, at worst polluting and encourage prompt leakage into the output (cf. [openai/whisper#1150](https://github.com/openai/whisper/discussions/1150)). Prompt target: continuous prose of 80-150 words, neutral register, anchored personal vocabulary, correct French punctuation, zero oral artifacts, a single block with no structure. The prompt must be derived from a real corpus, not guessed.

Before any prompt tweak, check the related parameters: `language` forced to `fr` on the `TranscriptionSettings.Engine.Language` side, `condition_on_previous_text` at its default, `suppress_tokens` (can remove French typographic characters `« » — '` if mis-tuned), `prepend_punctuations` / `append_punctuations`, and the 224-token prompt limit. **Never** put a `raw oral → clean` example in the prompt: Whisper produces a single text, the prompt shows what a clean output looks like. Raw oral correction is the LLM's job downstream, not Whisper's.

### whisper.cpp defaults and the `entropy_thold` trap

The native whisper.cpp fallback is now active: `temperature=0,0 / temperature_inc=0,2 / logprob_thold=-1,0 / entropy_thold=2,4`. The decoder automatically re-decodes failed segments at increasing temperature up to ≤ 1,0.

**`entropy_thold` is counter-intuitive**: the internal test is `entropy < threshold`, so HIGH threshold = STRICT (triggers fallback more often), LOW threshold = PERMISSIVE. Documented as a comment in the mapper on the Whisper backend side. Any proposal to tweak the thresholds must re-read this paragraph before touching the code.

### Hot-reload through SettingsService

The backend rebuilds its params on every `TranscribeAsync` call — `TranscriptionSettingsService.Instance.Current` snapshot read at the start of the call for free hot-reload, no model re-init.

## Non-negotiable UX rules

### Clipboard — 2 states max per transcription

The clipboard carries at most two successive contents over the duration of a transcription: the raw transcription, then the LLM-rewritten text if a profile is active. **Never accumulate token by token, never increment word by word.** The system clipboard history must stay clean. Consequence for any future LLM streaming: we replace the clipboard object in place, no append. The acceptable granularity is the full sentence (on period detection) or a regular interval of about 5 s, never token by token.

## Paste — UI Automation doctrine at Stop

Automatic paste is disabled by default on the settings side — the HUD always shows `Copied to clipboard` as a fallback when the user has not explicitly opted into paste. When paste is enabled, the policy is **clipboard safe by default, paste only if UIA confirms a text field**. Nothing is captured on Start anymore: no HWND target, no volatile focus. We trust the system state at the moment of Stop — the user had the whole recording + transcription + rewriting window to place their cursor.

`PasteFromClipboard` applies four ordered checks. All refuse to clipboard-only if false. (1) `GetForegroundWindow()` ≠ 0. (2) The foreground does not belong to the Deckle process. (3) `UIAutomation.IsFocusedElementTextEditable(out diag)` returns `true` — the probe reads `CUIAutomation.GetFocusedElement()` then `IUIAutomationElement.GetCurrentPropertyValue(UIA_ControlTypePropertyId)` and only validates `Edit` (50004) or `Document` (50030). (4) Full `SendInput` (4 events: `VK_CONTROL↓ VK_V↓ VK_V↑ VK_CONTROL↑`).

UIA is the canonical Windows accessibility API and answers the right question: *does this element accept input?* It works through classic Win32, WinForms, WPF, WinUI, Chromium (HTML `input`, `contenteditable`), Qt, Electron, UWP. A `class name` match misses modern frameworks — any proposal to go back to a `class name` match is to be refused.

Just before `PasteFromClipboard`, `OnReadyToPaste` is invoked synchronously and wired to `HudWindow.HideSync()`. The HUD is hidden in a blocking way (`DispatcherQueue` marshal + `ManualResetEventSlim`) before `SendInput` fires.

## Settings persistence

`TranscriptionSettingsService` loads and persists under `<UserDataRoot>/modules/transcription/settings.json` through `JsonSettingsStore<T>`.

## Internal structure

`TranscriptionSettings.cs` is the module's root POCO (seven nested sections: engine, speech detection, confidence, output filters, decoding, context, models directory). The internal `EngineSettings` class carries the backend bootstrap parameters (model, useGpu, language, initialPrompt) — its name avoids collision with the module name and reflects the role (config of the active ASR engine).

`TranscriptionSettingsService.cs` is the lazy singleton that loads and persists the settings + operates the on-disk migration. `ITranscriptionEngineHost.cs` is the bridge interface exposed to consumers (the app implements `AppTranscriptionEngineHost`). `WhisperPage.xaml(.cs)` and `ViewModels/WhisperViewModel.cs` carry the module's Settings UI today — the page is still whisper-centric (model picker, VAD settings, beam search); a generic agnostic page with a backend selector will come when a second backend is ready.

The `Engine/` folder hosts the orchestrator (`TranscriptionEngine.cs`) and its backend-agnostic helpers (`TextMetrics.cs`). The `IAsrBackend.cs` contract and the `TranscriptionResult.cs` DTO live there too — public surface consumed by the child modules. The `Setup/` folder now only carries the generic items (`Downloader.cs`, `ModelEntry.cs`); the whisper-specific catalogs (`SpeechModels`, `NativeRuntime`) have migrated to `Deckle.Transcription.Whisper.Setup`. The `Strings/en-US/` folder carries the `.resw` resources for the `x:Uid`s of `WhisperPage`. The `DeckleWhispSource` EventSource provider stays in the parent — its ETW name `Deckle.Whisp` is preserved so existing JSONL listeners don't break.
