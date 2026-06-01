---
name: claude-deckle-transcription-whisper
description: "Doctrine for Deckle.Transcription.Whisper, the IAsrBackend implementation via whisper.cpp (P/Invoke, native provisioning, model catalogs). Read before touching the Whisper backend, its native log compaction hook, or its provisioning catalogs."
type: agent-instructions
module: Deckle.Transcription.Whisper
---

# CLAUDE.md — Deckle.Transcription.Whisper

whisper.cpp ASR backend for the `Deckle.Transcription` module. Implements `IAsrBackend` behind the `WhisperBackend` class, encapsulates the entire P/Invoke machinery toward `libwhisper.dll`, and exposes the provisioning catalogs (`SpeechModels`, `NativeRuntime`) consumed by the first-run wizard.

Lives as a child module of `Deckle.Transcription` following the parent/children pattern already established by `Deckle.Diagnostics` → `Deckle.Diagnostics.Logging` + `Deckle.Diagnostics.Telemetry`. The parent carries the `IAsrBackend` contract, the `TranscriptionResult` DTO, and the `DeckleWhispSource` EventSource provider; the child carries the Whisper implementation. No reverse reference — the parent never sees this module; it is `Deckle.App` (the composition root) that instantiates `WhisperBackend` and injects it into `TranscriptionEngine`. Decision tracked in [ADR 0005](../../docs/adr/0005-pluggable-asr-backend-via-iasrbackend.md).

## Public surface

`WhisperBackend(ITranscriptionEngineHost host)` is the sole constructor. The backend reads its settings via `host.Transcription.Engine` (model, useGpu, language, initialPrompt) and resolves the model path via `host.ResolveModelsDirectory()`. Four methods implement `IAsrBackend`: `LoadModelAsync` (synchronous in practice for Whisper — `whisper_init` is blocking), `UnloadModel`, `TranscribeAsync`, `Dispose`. Three properties: `Name = "whisper"`, `IsModelLoaded`, `DetectedAccelerator` (`"CPU" | "Vulkan" | "CUDA" | "Metal"`).

The `Deckle.Transcription.Whisper.Setup` namespace exposes two types consumed by `Deckle.Setup` (the first-run wizard): `NativeRuntime` (provisioning of `libwhisper.dll` + ggml backends + MinGW runtime) and `SpeechModels` (catalog of downloadable Whisper `.bin` + Silero VAD). The `Deckle.Transcription.Whisper.Pinvoke` namespace is private to external use — it never leaves the backend.

## Inference pipeline

`TranscribeAsync(pcmSamples, segmentSink, ct)` chains: reset of accumulators (local segments, VAD parsing state), mapping of `TranscriptionSettings` → native `WhisperFullParams` via `WhisperParamsMapper`, wiring of the two callbacks (`new_segment_callback`, `abort_callback`), call to `whisper_full` under the `_transcribeLock` lock, forced stop of VAD/init stopwatches on early bail, free of native allocations, assembly of the `TranscriptionResult` (segments, full text, timings).

The `OnNewSegment` callback produces a `TranscriptionSegment` (Text, T0Cs, T1Cs, Confidence, NoSpeechProb), pushes it into `_segmentsLocal`, passes it to the `RepetitionDetector` (which may raise `_abortRequested`), invokes `segmentSink?.Invoke(segment)` for streaming to the orchestrator, and emits the detailed Verbose log (`p̄`, `min`, `dur`, `gap`).

## Native log compaction at model load

The `whisper_log_set` hook (installed once at backend construction, never uninstalled — it is a process-global callback) intercepts every line emitted by whisper.cpp. It routes three streams:

1. **Backend detection** — the first `ggml_vulkan:` / `ggml_cuda:` / `ggml_metal:` prefix encountered sticks in `_detectedBackend`. No match = `CPU`.
2. **VAD parsing** — `whisper_vad*` lines emitted while `_vadCapturing` is active are silenced and their values (speech duration, detected segments, % reduction, inference ms, mapping points) accumulated. At the `"Reduced audio from"` sentinel (end-of-VAD-module marker) or at the no-speech bail, a single consolidated `VadParsed` event is emitted.
3. **Init phase compaction** — four prefixes accumulate their respective lines until a different (or non-trackable) prefix arrives, which flushes the current phase as a single event. `whisper_init_with_params_no_state:` → `WhisperInitParamsParsed` (ID 101). `whisper_model_load:` → `WhisperModelLoadParsed` (102). `whisper_backend_init_gpu:` → `WhisperBackendInitParsed` (103). `whisper_init_state:` → `WhisperInitStateParsed` (104). A non-phase line (notably the standard `whisper_init_from_file_with_params_no_state:`) flushes the pending phase first before falling through to the normal switch level.

Orphan lines pass through a switch on `ggml_log_level`: ERROR (4) → `WhisperLogError`, WARN (3) → `WhisperLogWarning`, the rest → `WhisperLogVerbose`. Special case: `whisper_backend_init_gpu: no GPU found` (emitted by the creation of the secondary VAD context which hardcodes `use_gpu=false`) is downgraded to Verbose rather than Warn — benign but otherwise alarming.

## Repetition guard

`RepetitionDetector` is a binary classifier dedicated to the observed case: N consecutive identical segments (case- and whitespace-insensitive) on long audio with ambiguous trailing silence — the greedy decoder enters a loop where `logprob_thold` and `entropy_thold` do not bite (`p̂ ≈ 0.99`). The detector raises `_abortRequested`, the `abort_callback` returns `true` on whisper's next internal probe, `whisper_full` returns `0` with the segments produced before the bail.

The detector is whisper-specific (failure mode tuned for whisper.cpp). It therefore lives in this module and not in the parent — a future Voxtral backend will have its own characteristics and its own detector if necessary.

## Native runtime

The module depends on `libwhisper.dll` and the ggml backends (Vulkan priority, CPU fallback). The DLLs are not embedded in the repo — they are downloaded at first-run from the `native-vX.Y.Z` GitHub release of the Deckle repo or recompiled locally by the maintainer when an upstream upgrade is necessary. `scripts/lib/publish-native-runtime.ps1` packages the versioned bundle; the bootstrap code is in `Setup/NativeRuntime.cs`.

`WhisperPInvoke.cs` installs a `NativeLibrary.SetDllImportResolver` that loads `libwhisper.dll` from `<UserDataRoot>\native\` rather than from the exe directory. The `EntryDll` constant must stay synchronized with the literal string in each `[DllImport("libwhisper")]` — C# requires a literal constant in the attribute, so duplication is unavoidable.

## Known pitfalls

**whisper.cpp defaults and the `entropy_thold` pitfall** — the internal test is `entropy < threshold`, so a HIGH threshold = STRICT (triggers fallback more often), a LOW threshold = PERMISSIVE. Documented in `WhisperParamsMapper`. Any proposal to retune the thresholds must re-read the comment before acting.

**`ggml_log_level` mapping** — the `ggml_log_level` enum on the whisper.cpp / ggml side follows the order `NONE=0, DEBUG=1, INFO=2, WARN=3, ERROR=4, CONT=5`. Recurring pitfall: the intuition `1=Info, 2=Warn, 3=Error` is wrong — every `whisper_vad_*` and `whisper_full: *` line is emitted at INFO (2). The hook routes ERROR (4) → error, WARN (3) → warning, INFO/DEBUG (1-2) → verbose.

**GC of native delegates** — every delegate passed to whisper.cpp via `Marshal.GetFunctionPointerForDelegate` must be kept rooted in an instance field for the entire duration during which the native side holds the pointer. Without that, the GC can collect the thunk between two invocations and a native crash follows. `_logCallback`, `_segmentCallback`, `_abortCallback` are stored as fields for this reason.
