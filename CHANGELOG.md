# Changelog

All notable changes to Deckle are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
the project adheres to [Semantic Versioning](https://semver.org/). Deckle has no
public API: the version is read at the **user/behaviour** level, and during the
`0.x` phase any release may change behaviour (see the `deckle-versioning`
doctrine). Versions `0.2.0` and later are reconstructed from the git history;
earlier development predates this file.

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

[0.4.0]: https://github.com/louisfifre/deckle/compare/v0.3.5...v0.4.0
[0.3.5]: https://github.com/louisfifre/deckle/compare/v0.3.4...v0.3.5
[0.3.4]: https://github.com/louisfifre/deckle/compare/v0.3.3...v0.3.4
[0.3.3]: https://github.com/louisfifre/deckle/compare/v0.3.2...v0.3.3
[0.3.2]: https://github.com/louisfifre/deckle/compare/v0.3.1...v0.3.2
[0.3.1]: https://github.com/louisfifre/deckle/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/louisfifre/deckle/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/louisfifre/deckle/releases/tag/v0.2.0
