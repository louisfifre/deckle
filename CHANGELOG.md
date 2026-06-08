# Changelog

All notable changes to Deckle are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
the project adheres to [Semantic Versioning](https://semver.org/). Deckle has no
public API: the version is read at the **user/behaviour** level, and during the
`0.x` phase any release may change behaviour (see the `deckle-versioning`
doctrine). This file is generated from the Conventional-Commit history by
`scripts/lib/changelog.ps1` — do not edit it by hand.

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
