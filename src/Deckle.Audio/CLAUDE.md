---
name: claude-deckle-audio
description: "Doctrine for Deckle.Audio, the microphone capture and audio telemetry module. Read before touching microphone capture, RMS calibration, HUD level mapping, or the audio settings persistence path."
type: agent-instructions
module: Deckle.Audio
---

# CLAUDE.md — Deckle.Audio

Audio capture module. The current scope is 100% microphone: WASAPI capture via `waveInOpen`, polling on four 50 ms circular buffers, PCM16 → float conversion, real-time RMS computation feeding the HUD, tail RMS telemetry at Stop to detect an unplugged mic or low audio, heuristic auto-calibration over the last N sessions. The module also owns `AudioLevelMapper` (mutable statics that control the dBFS → perceptual level curve) — the app and the HUD read these statics on every vsync to render the chrono animation.

The module name is deliberately broader than its current content. The module was renamed `Deckle.Capture → Deckle.Audio` in May 2026 because `Capture` was a false generic with no primitive shareable with video capture (the future `Deckle.Vision` module). The name `Audio` remains correct if we later add loopback capture (PC output for audio triggers in ambient lighting), an audio output (confirmation TTS, monitoring), or any other audio signal processing. The internal classes kept their historical names (`CaptureSettings`, `CaptureResult`, `MicrophoneCapture`, `CaptureSettingsService`) — the consumer-facing API becomes `Deckle.Audio.CaptureSettings`, which reads as "audio capture settings in the Audio module".

## Consumer contract

The module exposes `MicrophoneCapture` (lifecycle orchestrator: `Probe()` for pre-flight, `Record(IAudioRecordingHost, CancellationToken)` for the session), `IAudioRecordingHost` (contract injected by the orchestrator — typically Whisp — that exposes the live settings consulted on every `Record()` entry), `CaptureResult` (audio float[] + mic telemetry + outcome), `CaptureSettings` + `CaptureSettingsService` (UI Settings → Recording page + auto-load), `AudioLevelMapper` (RMS → perceptual level mapping used by `Deckle.Hud`).

The fundamental pattern: the module does not know why we are capturing (transcription, future Ask-Ollama, anything else). It only knows how to capture cleanly and how to surface the telemetry needed to calibrate the user experience. Consumers implement `IAudioRecordingHost` to provide the device id, the duration cap, and the telemetry toggle, and they receive a complete `CaptureResult` on exit from `Record()`.

## Capture characteristics

Single non-parameterizable format: 16 kHz, mono, PCM16. This is the format Whisper expects and it remains valid for future uses (audio SLMs such as Voxtral also use this resolution). Four 50 ms circular buffers in pure polling (no managed queue, no event-driven). The RMS window size for HUD mapping is parameterizable through `CaptureSettings.LevelWindow` (RMS over a sliding window of N samples), but the polling cadence stays at 50 ms.

The RMS of each sub-window is emitted as a real-time event to feed the HUD animation via `AudioLevelMapper`. The `dBFS → [0, 1]` curve is defined by three statics (`MinDbfs`, `MaxDbfs`, `DbfsCurveExponent`) — the app pushes them on every setting change via `App.ApplyLevelWindow(...)`. These statics are mutable by design for runtime calibration from the Playground.

At the end of `Record()`, `MicrophoneTelemetryCalculator` computes a distributional rollup (p10, p25, p50, p75, p90, peak) over the whole session plus a tail RMS over the last 600 ms (used to detect an unplugged mic or very low audio). `MicrophoneCalibrationCalculator` adjusts the dBFS bounds over the last N sessions (`median(p25) - 5 dB` → MinDbfs, `median(p90) + 5 dB` → MaxDbfs, with clamps) so the perceptual curve stays adapted to the user's real environment.

## Transcription pre-processing

The `Preprocessing/` sub-folder (namespace `Deckle.Audio.Preprocessing`) hosts a terminal DSP stage that lifts and homogenises the captured signal before it reaches the ASR backend — see the *transcription pre-processing* entry in [`CONTEXT.md`](../../CONTEXT.md) for the term and its distinction from display level. It is post-capture and two-pass: the whole take is available at Stop (dictation is hotkey-driven, not streamed), so there is no real-time AGC. The stage is a pure `float[] → float[]` transform, inserted by the orchestrator (`Deckle.Transcription`) between `MicrophoneCapture.Record()` and `IAsrBackend.TranscribeAsync`, without touching how the backend windows the buffer.

`TranscriptionPreprocessor.Process(float[] pcm, PreprocessingSettings)` runs the chain and returns a `PreprocessingResult` — the new buffer plus input/output RMS, the makeup gain applied, and the output peak (the metrics the orchestrator emits). Each stage is `internal sealed`, pure, and individually bypassable: a 2nd-order RBJ high-pass (`HighPassFilter`, ~90 Hz, kills rumble, DC offset and plosives), a soft downward expander (`NoiseGate`, **off by default**), a soft-knee feed-forward compressor (`Compressor`, ratio ~2:1), then the two-pass move — measure the RMS of the compressed signal, derive the exact makeup gain to reach `TargetRmsDbfs` (clamped to `MaxMakeupGainDb`), apply it — and finally a peak limiter (`Limiter`) as an anti-clipping ceiling. The two-pass makeup is what makes the stage self-normalising per take: every recording lands at the same target level regardless of how loud it came in.

**Central guardrail** (the reason the defaults are gentle): compressing hard would raise the inter-word noise floor, and a lifted noise floor is documented as fuel for Whisper's silence hallucinations — the spurious « Sous-titres réalisés par… » boilerplate (cf. [`docs/research/research--whisper-dynamic-vad-distil-fr--2026-05-28.md`](../../docs/research/research--whisper-dynamic-vad-distil-fr--2026-05-28.md)). Hence the ~2:1 ratio, the gate off by default, and a conservative target. All stage parameters are conservative starting points to be refined by measurement, not values asserted as optimal.

The sub-module is **pure — it emits nothing**. It has no `EventSource` dependency, so the module invariant "all emissions go through `DeckleAudioSource.Log`" stays intact: the observability of a processed take is emitted by the *orchestrator* on its own provider (`DeckleWhispSource.TranscriptionPreprocessed`, Verbose / `Pipeline` keyword), built from the `PreprocessingResult` the function hands back.

**Settings.** `CaptureSettings.Preprocessing` (`PreprocessingSettings`, written with auto-properties so it round-trips cleanly through `JsonSettingsStore` — unlike the field-based `LevelWindowSettings`) carries the black-box `Enabled` toggle (**false** by default) plus the fixed per-stage parameters (not user-exposed — the black box has no knobs). It persists with the rest of the module under `modules/audio/settings.json`.

**Activation — user-decided, no auto-gate.** The toggle (`Enabled`) is the whole control: on means the DSP runs on every recording, off means it never touches the signal. There is no calibration delay and no automatic on/off decision — the stage self-adjusts (the two-pass makeup lands near 0 dB on a mic already at target, so it is a near no-op there). What helps the user decide is the **mic level check** (`MicLevelCheck` + `MicLevelTester`, surfaced on the Recording page): it records a short sample, runs the real DSP on it, and advises *recommended / marginal / not needed* from the deficit between the captured level and `TargetRmsDbfs` (threshold `MicLevelCheck.RecommendDeltaDb`, provisionally 6 dB). It proposes; the toggle stays the user's call. An earlier deferred-activation model (`Calibrating → Active/Dormant` over N takes) was removed in favour of this simpler shape.

**Corpus integrity** ([ADR-0006](../../docs/adr/0006-normalized-corpus-as-ml-dataset.md)). When the stage is active the orchestrator processes a *separate* `backendAudio` buffer and feeds that to the backend; the raw `audio` buffer stays the one written to the corpus WAV. The corpus therefore keeps an untouched raw baseline, so a processed variant can always be re-derived from it. The warmup path (embedded TTS clip) is never pre-processed: it is already clean.

## Observability

The module migrated to `EventSource` in wave 2 of the observability overhaul ([ADR-0003](../../docs/adr/0003-adopt-eventsource-for-observability.md)). All emissions go through `DeckleAudioSource.Log` — the `Deckle.Audio` provider exposed as a static singleton. No call to `TelemetryService.Instance` or `LogService.Instance` remains in the module.

Three emission zones. The waveIn loop milestones and anomalies (`RecordingStarted`, `CaptureStarted`, `EmptyBufferReceived`, `LowAudioDetected`, `CaptureLagDetected`, `DurationCapReached`, `RecordingCompleted`, `CaptureCompleted`). Device opening anomalies and empty telemetry (`MicrophoneOpenFailed`, `MicrophoneTelemetryEmpty`). The structured rollup per recording (`RecordingTailSummary` for the readable headline, `MicrophoneTelemetryRecorded` for the distributional payload with 14 fields flattened from the former `MicrophoneTelemetryPayload`). The module's settings persistence goes through the four events `SettingsLoaded` / `SettingsLoadComplete` / `SettingsLoadWarning` / `SettingsLoadError`, which receive the raw message forwarded by `JsonSettingsStore<T>` — this zone stays message-parameterized until `SettingsHost` migrates (wave 4).

`MicrophoneTelemetryPayload` lives in this module as the POCO carrier used by `MicrophoneTelemetryCalculator`, `CaptureResult`, the EventSource emission, and the auto-calibration ring of `TranscriptionEngine`.

Payload gating is still done by the orchestrator via `IAudioRecordingHost.MicrophoneTelemetryEnabled` (the "Log microphone" toggle in Settings → Telemetry). When the toggle is off, `MicrophoneTelemetryRecorded` is simply not emitted; the payload is nevertheless computed to feed auto-calibration.

## Persistence

`CaptureSettingsService` is a lazy singleton that loads and persists settings under `<UserDataRoot>/modules/audio/settings.json` via `JsonSettingsStore<CaptureSettings>`. Existing users with their file under `modules/capture/` are migrated automatically on first boot by `SettingsBootstrap.MigrateModuleFolder("capture", "audio")` (idempotent: no-op if the target already exists). The named mutex for multi-process sync is `Deckle-Settings-Audio-Save`. The log delegates injected into `JsonSettingsStore` now point to `DeckleAudioSource.Log.SettingsLoaded/SettingsLoadComplete/SettingsLoadWarning/SettingsLoadError` — the LogWindow source label becomes `AUDIO`, no longer `SETTINGS`, and the `[audio]` prefix that appeared at the start of legacy messages disappears because the tag already does the work.
