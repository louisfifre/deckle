# Changelog

All notable changes to Deckle are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
the project adheres to [Semantic Versioning](https://semver.org/). Deckle has no
public API: the version is read at the **user/behaviour** level, and during the
`0.x` phase any release may change behaviour (see the `deckle-versioning`
doctrine). This file is generated from the Conventional-Commit history by
`scripts/lib/changelog.ps1` — do not edit it by hand.

## [Unreleased]

### Added

- **scripts:** Generate changelog and release notes from git history
- **scripts:** Publish the installer exe as the release headline asset
- **input:** Bare Raw Input probe for the precision touchpad
- **input:** SendInput mouse injection primitive
- **shell:** Elevated startup via scheduled task
- **trackpad:** Three-finger drag domain module
- **app:** Compose the trackpad module
- **trackpad:** Settings page and navigation entry
- **trackpad:** Freeze calibrated values, retire the tuning expander
- **anytype:** Core library over the live PM space — client, frozen schema, gestures
- **mcp:** Stdio JSON-RPC host exposing the 13 PM tools
- **transcription:** Paragraph break on silence-cut utterances
- **notifications:** Notification catalogue, dispatcher, and interactive toast channel
- **playground:** Manual test surface for the notification toast channel
- **app:** Compose the notification dispatcher at boot
- **anytype:** Create projects and tasks from their default templates
- **input:** Keyboard and pointer raw input host with focus signals
- **core:** Describe the focused element for the autocorrect surface gate
- **autocorrect:** Typed-word tracking over the raw keyboard stream
- **autocorrect:** Conservative lexical gate for diacritics restoration
- **autocorrect:** Minimal-diff injection and decayed personal dictionary
- **autocorrect:** Left-context pair model and restoration eval harness
- **autocorrect:** Engine wiring - surface gate, correction revert, learning signals
- **mcp:** Self-documenting host copy
- **autocorrect:** Cli host - watch, inject, run, eval, data pipeline, enroll, dict
- **autocorrect:** Derived lexical artifacts (Lexique, Norvig, Wikipedia FR pairs)
- **autocorrect:** Calibrate the context margin from the eval matrix
- **anytype:** Select options are applied, never created
- **taskbar-cover:** Edge-aware cover band domain module
- **shell:** Taskbar cover switch in the tray menu
- **app:** Compose the taskbar cover module
- **autocorrect:** Trace mode attributes every key event by origin
- **core:** Add a dedicated diagnostics directory under the data root
- **app:** Persist the setup narrative and critical errors locally
- **settings:** Link to the local diagnostics folder
- **setup:** Offer the diagnostics folder on a failed first run
- **anytype:** Replace_section — heading-located body edit, verified
- **anytype:** Add dialogue chat tools
- **autocorrect:** N-gram left-context disambiguation and precision-first eval
- **autocorrect:** CamemBERT MLM reranker probe (offline)
- **autocorrect:** Post-sentence reranker stage in the offline eval
- **autocorrect:** Proper-noun caps guard for the lexical gate
- **autocorrect:** Reranker frequency prior and eval tuning flags
- **autocorrect:** Offline dry-run command
- **scripts:** Add README stats automation
- **anytype:** Serialize cross-session writes with a file lock
- **autocorrect:** Observation-live harvest command
- **autocorrect:** Optional Morphalou inflected-form overlay
- **app:** Wire the autocorrect engine into the app

### Changed

- **tray-menu:** Split milestones from their Verbose detail mirrors
- **lighting:** Split milestones from their Verbose detail mirrors
- **audio:** Split milestones from their Verbose detail mirrors
- **shell:** Split milestones from their Verbose detail mirrors
- **input:** Split milestones from their Verbose detail mirrors
- **trackpad:** Split milestones from their Verbose detail mirrors
- **threading:** Split milestones from their Verbose detail mirrors
- **anytype:** Split milestones from their Verbose detail mirrors
- **hud:** Split the HideSync-timeout warning from its Verbose detail
- **settings:** Split milestones from their Verbose detail mirrors
- **ambient:** Split milestones from their Verbose detail mirrors
- **llm:** Split milestones from their Verbose detail mirrors
- **app:** Split milestones from their Verbose detail mirrors
- **whisp:** Split milestones from their Verbose detail mirrors
- **vision:** Split milestones from their Verbose detail mirrors
- **chrono:** Split the pilot milestone from its Verbose detail
- **vad:** Split milestones from their Verbose detail mirrors
- **resource:** Split the leak-suspect warning from its Verbose detail
- **playground:** Type the diagnostic event channels
- **setup:** Type the wizard event channels
- **settings:** Type the per-setting change sub-channel
- **autocorrect:** Extract OS-port interfaces for the test seam
- **diagnostics:** Self-create the JSONL sink parent directory
- **hud:** Share one cursor-movement signal across the HUD surfaces
- **app:** Split LogWindow into Model/Interaction/Chrome partials
- **tray-menu:** Split TrayContextMenuHost into Window/Flyout/Show/Measure partials
- **ambient:** Split AmbientEngine.Lifecycle event handlers into partials
- **whisper:** Extract WhisperNativeLogCompactor from WhisperBackend
- **transcription:** Split TranscriptionEngine.Pipeline into Finalize/Telemetry partials
- Collapse to one namespace per module
- **hud:** Split HudWindow and organize into Chrono/Windows/Model
- **settings:** Organize into Dialogs/Pages/Controls/Persistence
- **playground:** Organize Views into Ambient/Hud

### Fixed

- **transcription:** Pin SHA-256 verification on Whisper model downloads
- **lighting:** Validate the bridge IP at HueBridgeClient construction
- **installer:** Refuse cmd metacharacters in the delayed self-delete
- **transcription:** Fail closed when the native runtime DLL is absent
- **audio:** Unwind partial buffer prep when capture setup throws
- **setup:** Download silero_vad.onnx instead of ggml binary
- **trackpad:** Mirror gesture-button Loc keys into the app resw
- **trackpad:** Rework the Settings page after first hands-on
- **anytype:** Tolerate the bare-string list-add response on epic attach
- **notifications:** Self-settling prompts, live availability gate, complete narrative
- **notifications:** Mirror descriptor Loc keys into the App resw, harden Loc misses
- **mcp:** Link copy states the real pair matrix; instructions carry the property discipline
- **autocorrect:** Land tracker state before raising commit events
- **autocorrect:** Chorded editing keys decode as shortcuts
- **autocorrect:** Corrections no longer feed their own defeat
- **autocorrect:** Make the live run path diagnosable
- **taskbar-cover:** Serialize host restarts and unblock Start from shell hangs
- **taskbar-cover:** Observe timer arming failures
- **taskbar-cover:** Pin the pump imports to their Unicode entry points
- **app:** Detach and flush taskbar cover settings at shutdown
- **taskbar-cover:** Hold the provider to the Verbose/Info separation
- **input:** Guard the parser-failure detail behind its braces
- **anytype:** Invert the rapport↔task link, derive the project through tasks
- **app:** Always surface the streaming transcript in the log
- **app:** Add missing Setup_OpenDiagnosticsFolder to the root resource map
- **app:** Register always-on local sinks before settings migration

## [0.4.4](https://github.com/louisfifre/deckle/compare/v0.4.3...v0.4.4) — 2026-06-07

### Added

- **scripts:** Add a GitHub Release action to the dev menu

### Fixed

- **transcription:** Start chrono and duration on real capture start

## [0.4.3](https://github.com/louisfifre/deckle/compare/v0.4.2...v0.4.3) — 2026-06-07

### Added

- **transcription:** Dynamic hangover ramp + observable streaming pipeline
- **transcription:** Detect AB-AB period-2 repetition loops
- **inference:** Silero VAD v5 ONNX module
- **transcription:** Trim streaming utterances with the external Silero VAD
- **transcription:** Surface an untrimmed streaming take; test Reset
- **transcription:** Make the external Silero VAD the speech-detection toggle
- **transcription:** Expose the Silero VAD parameters and log span counts

### Changed

- **hud:** Decouple the chrono lifecycle from the paint states
- **playground:** Drive the chrono clock explicitly in HUD previews
- **hud:** Split HudChrono into per-concern partials
- **diagnostics:** Name ETW providers Deckle-<component>, not Deckle.<module>
- **audio:** Sharpen the capture-lag probe to attribute a cause
- **vad:** Autonomous module, kill the dead whisper-internal VAD

### Fixed

- **app:** Switch the HUD to Transcribing the instant Stop is pressed
- **hud:** Serialize the chrono stroke lifecycle against the RMS pump
- **inference:** Dispose SessionOptions when the Silero session fails to construct
- **transcription:** Checksum-verify the Silero VAD download and self-heal a corrupt model
- **inference:** Run the v6.2 Silero VAD model and verify the on-disk build
- **app:** Keep the streaming firehose log gate whole across the VAD split

## [0.4.2](https://github.com/louisfifre/deckle/compare/v0.4.1...v0.4.2) — 2026-06-05

### Fixed

- **scripts:** Serialize the publish build to avoid the WinAppSDK PRI race
- **hud:** Suppress stale delayed z-order probes
- **build:** Dedupe self-contained project references

## [0.4.1](https://github.com/louisfifre/deckle/compare/v0.4.0...v0.4.1) — 2026-06-04

### Added

- **installer:** Add NativeAOT download-stub installer
- **diagnostics:** Roll app journal by line count into a kept archive
- **telemetry:** Route the post-DSP distribution to its own channel

### Fixed

- **scripts:** Mark 0.x releases as pre-release

## [0.4.0](https://github.com/louisfifre/deckle/compare/v0.3.5...v0.4.0) — 2026-06-04

### Added

- **vision:** Roll up the capture heartbeat over a 5s window
- **observability:** Gate the heartbeat behind the capture toggle
- **ambient:** Name the stop reason in the pipeline-stopped milestone
- **audio:** Add transcription pre-processing DSP module
- **whisp:** Run DSP pre-processing before transcription
- **settings:** Expose the transcription pre-processing toggle on Recording
- **audio:** Add a mic level check for the pre-processing toggle
- **settings:** Add the mic level check to the Recording page
- **audio:** Make the pre-processing toggle take effect immediately
- **whisp:** Let the audio corpus follow the processed signal
- **scripts:** Streamline deckle maintenance stats
- **audio:** Emit live capture frames for stream consumers
- **transcription:** Add energy segmenter producing utterances
- **transcription:** Add optional priming context to the ASR contract
- **transcription:** Add streaming utterance pipeline
- **transcription:** Expose streaming strategy and segmenter in Settings
- **observability:** Add post-DSP microphone telemetry aggregate
- **transcription:** Run DSP preprocessing in the streaming pipeline
- **observability:** Tag corpus rows with raw vs processed audio

### Changed

- **transcription:** Extract pipeline seam and shared finalize
- **composition:** Split oversized host and HUD files
- **transcription:** Readable per-take streaming logs
- Split hue and vision oversized files

### Fixed

- **transcription:** Harden the streaming pipeline drain and ordering
- **transcription:** Hide segmenter params when streaming is off
- **app:** Close secondary windows for real
- **hud:** Reassert topmost on show
- **ui:** Sync secondary window navigation panes
- **ambient:** Treat stale matching Hue echo as echo, not external
- **scripts:** Align publish folder name with the release artifact
- **hud:** Stabilize post-build topmost show

## Earlier history

Versions 0.2.0 – 0.3.5 — the WhispUI genesis and the early Deckle cycles
(hotkey transcription, ambient lighting, observability) — predate this
generated changelog and are not itemised here. See the git history.
