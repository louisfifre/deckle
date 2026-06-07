# Changelog

All notable changes to Deckle are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
the project adheres to [Semantic Versioning](https://semver.org/). Deckle has no
public API: the version is read at the **user/behaviour** level, and during the
`0.x` phase any release may change behaviour (see the `deckle-versioning`
doctrine). Versions `0.2.0` and later are reconstructed from the git history;
earlier development predates this file.

## [0.4.4] — 2026-06-07

A recording chrono that starts on your voice.

### Fixed

- **Recording chrono.** The on-screen timer started a touch before capture
  actually began, drifting ahead of the audio by the microphone's open latency;
  it now starts the instant capture goes live, so the elapsed time matches the
  recording.

## [0.4.3] — 2026-06-07

Silence-aware streaming transcription.

### Added

- **Voice-activity detection.** An external Silero VAD becomes the
  speech-detection toggle on the Recording page: it trims silence from each
  streaming utterance, with its parameters exposed and a dynamic hangover ramp
  that holds the tail of speech before closing a span. The model is downloaded
  on demand, SHA-256–verified and self-heals if the on-disk file is corrupt.
- **Repetition guard.** Transcription output now detects AB-AB period-2
  repetition loops.

### Fixed

- **Inference robustness.** A failed Silero session construction disposes its
  options cleanly instead of leaking them.

## [0.4.2] — 2026-06-05

Maintenance release for the build and repository doctrine.

### Changed

- **Build layout.** Module `bin/` and `obj/` outputs now consolidate under the
  root `artifacts/` tree, and the build/run, clean and stats scripts follow
  that layout so source folders stay readable.
- **Agent doctrine.** Root and module `CLAUDE.md` files now keep durable intent,
  non-obvious decisions and silent pitfalls; the Deckle skills use the compact
  Intent/How format, with `deckle-interface` replacing the older XAML-specific
  doctrine.

### Removed

- **Obsolete agent material.** Removed stale generic agents, generic
  senior/tdd skills, dated review notes, `SECURITY.md`, `deckle-workflow` and
  journal detail that had become noise instead of durable context.

### Fixed

- **HUD z-order.** Delayed topmost probes no longer report stale state after
  the HUD lifecycle has moved on.
- **Self-contained builds.** Library project references are marked RID-agnostic
  so self-contained app builds do not schedule duplicate project-reference
  requests for the same outputs.

## [0.4.1] — 2026-06-04

A downloadable installer.

### Added

- **Installer.** A standalone download stub installs Deckle without cloning or
  building. It resolves the latest GitHub release, downloads and SHA-256–verifies
  the app, installs per-user without admin rights, adds a Start Menu shortcut and
  an Installed-apps entry, then launches the app — binaries and data/models in
  separate, relocatable folders. Re-running it updates to the newest release;
  uninstalling reverses everything and keeps your models by default.

## [0.4.0] — 2026-06-02

The streaming transcription socle.

### Added

- **Streaming transcription.** An energy-based segmenter splits a take into
  utterances and hands each one to the ASR as it closes, with optional priming
  context carried across windows. The strategy and the segmenter parameters are
  selectable on the Recording settings.
- **Transcription pre-processing (DSP).** An opt-in conditioning stage
  (high-pass, gate, compression, makeup gain, limiter) runs before transcription
  so low or uneven microphone levels transcribe more reliably. Toggled on the
  Recording page with a mic level check, applied immediately; the audio corpus
  follows the processed signal.
- **Capture heartbeat.** The screen-capture loop emits a rolled-up
  fps/percentile heartbeat over a 5 s window, gated behind the capture toggle.

### Changed

- The ambient pipeline-stopped milestone now names its stop reason, and
  per-take streaming logs are readable rather than per-frame noise.

### Fixed

- Hardened the streaming pipeline's drain and ordering.
- The segmenter parameters are hidden on the Recording page when streaming is
  off.

## [0.3.5] — 2026-05-31

Observability presentation tiers and a self-describing, bounded `app.jsonl`
journal; reliable LogWindow copy on a full selection.

## [0.3.4] — 2026-05-31

A WinUI 3 tray context menu (`Deckle.Shell.TrayMenu`) aligned to Windows 11
density; a screen-capture stall detector and format-aware duplication recovery;
model warm-up on the first hotkey instead of at boot.

## [0.3.3] — 2026-05-28

No user-facing change. Research only: three ASR proof-of-concept explorations
(Voxtral, Phi-4) via ONNX/DirectML, kept under `benchmark/`.

## [0.3.2] — 2026-05-28

Transverse observability sub-providers — windowing, threading, theme,
cancellation, resource and network — instrumented across the app.

## [0.3.1] — 2026-05-24

The transcription corpus reorganised as a normalised ML dataset with routed
JSONL destinations; ambient pipeline resilience and a zone-sampling thickness
setting.

## [0.3.0] — 2026-05-23

Ambient lighting: screen-capture-driven Philips Hue control — DXGI Output
Duplication capture, an OKLCh colour-science pipeline, multi-light zones with
Hue entertainment auto-fill, HDR tuning, brightness curves and mode presets,
surfaced in Settings and the Playground.

## [0.2.0] — 2026-05-02

Initial tracked release: hotkey voice transcription (whisper.cpp), the timer
HUD, system tray and global hotkeys, settings, and EventSource-based
observability.

[0.4.4]: https://github.com/louisfifre/deckle/compare/v0.4.3...v0.4.4
[0.4.3]: https://github.com/louisfifre/deckle/compare/v0.4.2...v0.4.3
[0.4.2]: https://github.com/louisfifre/deckle/compare/v0.4.1...v0.4.2
[0.4.1]: https://github.com/louisfifre/deckle/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/louisfifre/deckle/compare/v0.3.5...v0.4.0
[0.3.5]: https://github.com/louisfifre/deckle/compare/v0.3.4...v0.3.5
[0.3.4]: https://github.com/louisfifre/deckle/compare/v0.3.3...v0.3.4
[0.3.3]: https://github.com/louisfifre/deckle/compare/v0.3.2...v0.3.3
[0.3.2]: https://github.com/louisfifre/deckle/compare/v0.3.1...v0.3.2
[0.3.1]: https://github.com/louisfifre/deckle/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/louisfifre/deckle/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/louisfifre/deckle/releases/tag/v0.2.0
