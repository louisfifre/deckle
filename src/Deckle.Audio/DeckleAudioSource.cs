using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Audio;

// Audio module provider. Covers microphone capture (waveIn loop, low-audio
// detection, capture lag, duration cap), per-recording microphone telemetry
// summary (14-field structured payload), and module settings persistence.
//
// Singleton statique, instanciation thread-safe via `static readonly`.
// The ETW manifest is self-describing (inherited from DeckleEventSource);
// snake_case parameter names directly become JSON keys in the payload emitted
// by JsonlSink.
//
// Naming conventions for this provider:
//   - Concise milestones (capitalized, Informational level) → past-tense verb
//     suffix: RecordingStarted, RecordingCompleted.
//   - Structured Verbose mirrors → mirrored -Started / -Completed suffix:
//     CaptureStarted, CaptureCompleted.
//   - Structured payload (heartbeat) → -Recorded suffix:
//     MicrophoneTelemetryRecorded.
//   - Anomalies → past-tense condition name:
//     EmptyBufferReceived, LowAudioDetected, CaptureLagDetected,
//     DurationCapReached, MicrophoneOpenFailed,
//     MicrophoneTelemetryEmpty.
//   - Module settings persistence → Settings- prefix:
//     SettingsLoaded, SettingsLoadComplete, SettingsLoadWarning,
//     SettingsLoadError.
[EventSource(Name = "Deckle-Audio")]
public sealed class DeckleAudioSource : DeckleEventSource
{
    public static readonly DeckleAudioSource Log = new();

    private DeckleAudioSource() { }

    // ── EventIds: sequential from 1, never reused ───────────────────────
    public const int EvtRecordingStarted          = 1;
    public const int EvtCaptureStarted            = 2;
    public const int EvtEmptyBufferReceived       = 3;
    public const int EvtLowAudioDetected          = 4;
    public const int EvtCaptureLagDetected        = 5;
    public const int EvtDurationCapReached        = 6;
    public const int EvtMicrophoneOpenFailed      = 7;
    public const int EvtRecordingCompleted        = 8;
    public const int EvtCaptureCompleted          = 9;
    public const int EvtMicrophoneTelemetryEmpty  = 10;
    public const int EvtRecordingTailSummary      = 11;
    public const int EvtMicrophoneTelemetryRecorded = 12;
    public const int EvtSettingsLoaded            = 13;
    public const int EvtSettingsLoadComplete      = 14;
    public const int EvtSettingsLoadWarning       = 15;
    public const int EvtSettingsLoadError         = 16;
    // Milestones keep their original id; the Verbose mirrors added for the
    // Verbose/Info separation take fresh ids 17-23 appended at the end of the
    // sequence. IDs are public in the ETW manifest; never reuse an id.
    public const int EvtRecordingCompletedDetail  = 17;
    public const int EvtEmptyBufferReceivedDetail = 18;
    public const int EvtLowAudioDetectedDetail    = 19;
    public const int EvtCaptureLagDetectedDetail  = 20;
    public const int EvtDurationCapReachedDetail  = 21;
    public const int EvtMicrophoneOpenFailedDetail = 22;
    public const int EvtRecordingTailSummaryDetail = 23;
    // Speaker render (waveOut) — open-failure milestone + verbose mirror.
    public const int EvtSpeakerOpenFailed          = 24;
    public const int EvtSpeakerOpenFailedDetail    = 25;
    // File transcription — Media Foundation decode of an audio file to 16 kHz
    // mono float. Milestone + verbose mirror for both the success and the
    // failure path.
    public const int EvtAudioFileDecoded           = 26;
    public const int EvtAudioFileDecodedDetail     = 27;
    public const int EvtAudioFileDecodeFailed      = 28;
    public const int EvtAudioFileDecodeFailedDetail = 29;
    public const int EvtEmptyBufferRecovered       = 30;
    public const int EvtEmptyBufferEpisodeDetail   = 31;
    public const int EvtCaptureLagRecovered        = 32;
    public const int EvtCaptureLagEpisodeDetail    = 33;

    // ── Recording lifecycle (milestones + verbose mirrors) ──────────────

    [Event(EvtRecordingStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Recording started")]
    public void RecordingStarted()
    {
        if (IsEnabled()) WriteEvent(EvtRecordingStarted);
    }

    [Event(EvtCaptureStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "capture start | sample_rate=16 kHz | channels=mono")]
    public void CaptureStarted()
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtCaptureStarted);
    }

    [Event(EvtRecordingCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Recording complete")]
    public void RecordingCompleted()
    {
        if (IsEnabled()) WriteEvent(EvtRecordingCompleted);
    }

    [Event(EvtRecordingCompletedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "recording complete | total_sec={0:F1}")]
    public void RecordingCompletedDetail(double total_sec)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtRecordingCompletedDetail, total_sec);
    }

    [Event(EvtCaptureCompleted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "capture complete | audio_sec={0:F1} | buffers={1} | bytes={2} | rms_avg={3:F4} | rms_peak={4:F4} | dbfs_avg={5:F1}")]
    public void CaptureCompleted(double audio_sec, int buffers, int bytes, double rms_avg, double rms_peak, double dbfs_avg)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtCaptureCompleted, audio_sec, buffers, bytes, rms_avg, rms_peak, dbfs_avg);
    }

    // Optional per-take diagnosis. Both the sentence and its measurements are
    // admitted detail; neither belongs to the permanent workflow timeline.
    [Event(EvtRecordingTailSummary,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "{0}")]
    public void RecordingTailSummary(string tail_headline)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtRecordingTailSummary, tail_headline);
    }

    [Event(EvtRecordingTailSummaryDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "recording tail | tail_ms={0} | tail_dbfs={1:F1}")]
    public void RecordingTailSummaryDetail(int tail_ms, double tail_dbfs)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtRecordingTailSummaryDetail, tail_ms, tail_dbfs);
    }

    // ── Anomalies Captured In The waveIn Loop ───────────────────────────

    [Event(EvtEmptyBufferReceived,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "An empty capture buffer was received")]
    public void EmptyBufferReceived()
    {
        if (IsEnabled()) WriteEvent(EvtEmptyBufferReceived);
    }

    [Event(EvtEmptyBufferReceivedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "empty buffer | index={0}")]
    public void EmptyBufferReceivedDetail(int index)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtEmptyBufferReceivedDetail, index);
    }

    [Event(EvtEmptyBufferRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Capture buffers recovered")]
    public void EmptyBufferRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtEmptyBufferRecovered);
    }

    [Event(EvtEmptyBufferEpisodeDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "empty buffer episode | occurrences={0} | recovered={1}")]
    public void EmptyBufferEpisodeDetail(int occurrences, bool recovered)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtEmptyBufferEpisodeDetail, occurrences, recovered);
    }

    [Event(EvtLowAudioDetected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "No healthy voice was detected at the start of the recording")]
    public void LowAudioDetected()
    {
        if (IsEnabled()) WriteEvent(EvtLowAudioDetected);
    }

    [Event(EvtLowAudioDetectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "low audio detected | recording_ms={0} | min_sustained_ms={1} | dbfs_threshold={2}")]
    public void LowAudioDetectedDetail(int recording_ms, int min_sustained_ms, double dbfs_threshold)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtLowAudioDetectedDetail, recording_ms, min_sustained_ms, dbfs_threshold);
    }

    // gc0/gc1/gc2 are the collections that happened DURING the lagging
    // iteration (the one whose body caused the pile-up), not a running total
    // since recording start — so a non-zero value indicts that GC directly.
    [Event(EvtCaptureLagDetected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The capture loop fell behind the microphone")]
    public void CaptureLagDetected()
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLagDetected);
    }

    [Event(EvtCaptureLagDetectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "capture lag | buffers_ready={0} | iter={1} | wait_ms={2} | prev_iter_ms={3} | gc0={4} | gc1={5} | gc2={6}")]
    public void CaptureLagDetectedDetail(int buffers_ready, long iter, long wait_ms, long prev_iter_ms, int gc0, int gc1, int gc2)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtCaptureLagDetectedDetail, buffers_ready, iter, wait_ms, prev_iter_ms, gc0, gc1, gc2);
    }

    [Event(EvtCaptureLagRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The capture loop recovered")]
    public void CaptureLagRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLagRecovered);
    }

    [Event(EvtCaptureLagEpisodeDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "capture lag episode | occurrences={0} | recovered={1}")]
    public void CaptureLagEpisodeDetail(int occurrences, bool recovered)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtCaptureLagEpisodeDetail, occurrences, recovered);
    }

    [Event(EvtDurationCapReached,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Recording stopped at the maximum duration")]
    public void DurationCapReached()
    {
        if (IsEnabled()) WriteEvent(EvtDurationCapReached);
    }

    [Event(EvtDurationCapReachedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "duration cap reached | audio_sec={0:F1} | cap_sec={1}")]
    public void DurationCapReachedDetail(double audio_sec, int cap_sec)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)(Keywords.Capture | Keywords.Lifecycle)))
            WriteEvent(EvtDurationCapReachedDetail, audio_sec, cap_sec);
    }

    [Event(EvtMicrophoneOpenFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "The microphone could not be opened")]
    public void MicrophoneOpenFailed()
    {
        if (IsEnabled()) WriteEvent(EvtMicrophoneOpenFailed);
    }

    [Event(EvtMicrophoneOpenFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "microphone open failed | mmsys_err={0}")]
    public void MicrophoneOpenFailedDetail(uint mmsys_err)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)(Keywords.Capture | Keywords.Lifecycle)))
            WriteEvent(EvtMicrophoneOpenFailedDetail, mmsys_err);
    }

    // Speaker render — the waveOut device could not be opened (no render device,
    // or an exclusive-mode conflict). Output path of the read-aloud feature;
    // mirrors the microphone open-failure pair above.
    [Event(EvtSpeakerOpenFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Push | Keywords.Lifecycle),
           Message = "The speaker could not be opened for playback")]
    public void SpeakerOpenFailed()
    {
        if (IsEnabled()) WriteEvent(EvtSpeakerOpenFailed);
    }

    [Event(EvtSpeakerOpenFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Push | Keywords.Lifecycle),
           Message = "speaker open failed | mmsys_err={0}")]
    public void SpeakerOpenFailedDetail(uint mmsys_err)
    {
        if (IsEnabled()) WriteEvent(EvtSpeakerOpenFailedDetail, mmsys_err);
    }

    // ── Audio-file decode (file transcription) ──────────────────────────────
    //
    // AudioFileDecoder decodes a picked audio file to 16 kHz mono float via
    // Media Foundation, feeding the same pipeline dictation does. Success is a
    // past-tense milestone with its Verbose mirror; failure follows
    // MicrophoneOpenFailed's shape — a milestone plus a mirror carrying the
    // status and raw HRESULT — but stays Warning, not Error, because a bad file
    // is an expected user outcome, not a broken device.

    [Event(EvtAudioFileDecoded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Decoded an audio file for transcription")]
    public void AudioFileDecoded()
    {
        if (IsEnabled()) WriteEvent(EvtAudioFileDecoded);
    }

    [Event(EvtAudioFileDecodedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "audio file decoded | source={0} | duration_sec={1:F1} | decoded_samples={2} | elapsed_ms={3}")]
    public void AudioFileDecodedDetail(string source, double duration_sec, int decoded_samples, long elapsed_ms)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture))
            WriteEvent(EvtAudioFileDecodedDetail, source, duration_sec, decoded_samples, elapsed_ms);
    }

    [Event(EvtAudioFileDecodeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Could not decode the audio file")]
    public void AudioFileDecodeFailed()
    {
        if (IsEnabled()) WriteEvent(EvtAudioFileDecodeFailed);
    }

    [Event(EvtAudioFileDecodeFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "audio file decode failed | status={0} | hr=0x{1:X8}")]
    public void AudioFileDecodeFailedDetail(string status, int hr)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)(Keywords.Capture | Keywords.Lifecycle)))
            WriteEvent(EvtAudioFileDecodeFailedDetail, status, hr);
    }

    // A short take with no complete RMS window is ordinary per-take detail, not
    // a durable microphone incident.
    [Event(EvtMicrophoneTelemetryEmpty,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "The recording was too short to measure microphone levels")]
    public void MicrophoneTelemetryEmpty()
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat))
            WriteEvent(EvtMicrophoneTelemetryEmpty);
    }

    // ── Structured Heartbeat: Microphone Telemetry Payload ──────────────
    //
    // Remplace TelemetryService.Microphone(MicrophoneTelemetryPayload).
    // The 14 properties of the legacy POCO record become primitive typed
    // parameters. EventSource does not accept complex types in signatures, so
    // the payload is flattened.

    [Event(EvtMicrophoneTelemetryRecorded,
           Level = EventLevel.Verbose,
           Tags = ObservationTags.Dataset,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "Mic telemetry over {0:F1}s ({1} samples @20Hz): min={2:F1} p10={3:F1} p25={4:F1} p50={5:F1} p75={6:F1} p90={7:F1} max={8:F1} dBFS | mean RMS={9:F4} ({10:F1} dBFS)")]
    public void MicrophoneTelemetryRecorded(
        double duration_seconds,
        int    samples,
        double min_dbfs,
        double p10_dbfs,
        double p25_dbfs,
        double p50_dbfs,
        double p75_dbfs,
        double p90_dbfs,
        double max_dbfs,
        double mean_rms,
        double mean_dbfs,
        double tail_rms,
        double tail_dbfs,
        string tail_state)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtMicrophoneTelemetryRecorded,
            duration_seconds, samples,
            min_dbfs, p10_dbfs, p25_dbfs, p50_dbfs, p75_dbfs, p90_dbfs, max_dbfs,
            mean_rms, mean_dbfs, tail_rms, tail_dbfs, tail_state);
    }

    // ── Module Settings Persistence ─────────────────────────────────────
    //
    // The legacy pattern sent these lines through LogSource.Settings with an
    // "[audio]" prefix written in the message. The new architecture brings
    // them back to the provider that emits them (DeckleAudioSource is in
    // Deckle.Audio, and so is SettingsService); the source label becomes AUDIO
    // through the LogWindow bridge, no longer SETTINGS. The message prefix
    // disappears because the tag already does the work.
    //
    // Exception to the "strict-typed per operation" doctrine. The
    // JsonSettingsStore<T> delegates in Deckle.Core are Action<string> and call
    // these 4 methods with a parameterized message; at the delegate call site,
    // I cannot distinguish "Settings loaded" from "Settings initialized
    // (defaults)" or "reloaded from disk". This area is therefore typed by
    // level and keyword, not by operation. The clean redesign comes in wave 4
    // when SettingsHost / JsonSettingsStore themselves move to a direct
    // EventSource contract, and these 4 events will then be replaced by their
    // per-operation equivalents.

    [Event(EvtSettingsLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoaded(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoaded, message);
    }

    [Event(EvtSettingsLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadComplete(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadComplete, message);
    }

    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }
}
