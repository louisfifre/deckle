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
// by JsonlEventListener.
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

    // ── Recording lifecycle (milestones + verbose mirrors) ──────────────

    [Event(EvtRecordingStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Recording start")]
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
        if (IsEnabled()) WriteEvent(EvtCaptureStarted);
    }

    [Event(EvtRecordingCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Recording complete ({0:F1} s)")]
    public void RecordingCompleted(double total_sec)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingCompleted, total_sec);
    }

    [Event(EvtCaptureCompleted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "capture complete | audio_sec={0:F1} | buffers={1} | bytes={2} | rms_avg={3:F4} | rms_peak={4:F4} | dbfs_avg={5:F1}")]
    public void CaptureCompleted(double audio_sec, int buffers, int bytes, double rms_avg, double rms_peak, double dbfs_avg)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureCompleted, audio_sec, buffers, bytes, rms_avg, rms_peak, dbfs_avg);
    }

    [Event(EvtRecordingTailSummary,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "{0} (last {1} ms at {2:F1} dBFS)")]
    public void RecordingTailSummary(string tail_headline, int tail_ms, double tail_dbfs)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingTailSummary, tail_headline, tail_ms, tail_dbfs);
    }

    // ── Anomalies Captured In The waveIn Loop ───────────────────────────

    [Event(EvtEmptyBufferReceived,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "empty buffer | index={0}")]
    public void EmptyBufferReceived(int index)
    {
        if (IsEnabled()) WriteEvent(EvtEmptyBufferReceived, index);
    }

    [Event(EvtLowAudioDetected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "low audio detected | recording_ms={0} | no healthy voice ≥{1} ms above {2} dBFS")]
    public void LowAudioDetected(int recording_ms, int min_sustained_ms, double dbfs_threshold)
    {
        if (IsEnabled()) WriteEvent(EvtLowAudioDetected, recording_ms, min_sustained_ms, dbfs_threshold);
    }

    // gc0/gc1/gc2 are the collections that happened DURING the lagging
    // iteration (the one whose body caused the pile-up), not a running total
    // since recording start — so a non-zero value indicts that GC directly.
    [Event(EvtCaptureLagDetected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "capture lag | buffers_ready={0} iter={1} wait_ms={2} prev_iter_ms={3} gc0={4} gc1={5} gc2={6}")]
    public void CaptureLagDetected(int buffers_ready, long iter, long wait_ms, long prev_iter_ms, int gc0, int gc1, int gc2)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLagDetected, buffers_ready, iter, wait_ms, prev_iter_ms, gc0, gc1, gc2);
    }

    [Event(EvtDurationCapReached,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "duration cap reached | audio_sec={0:F1} | cap_sec={1}")]
    public void DurationCapReached(double audio_sec, int cap_sec)
    {
        if (IsEnabled()) WriteEvent(EvtDurationCapReached, audio_sec, cap_sec);
    }

    [Event(EvtMicrophoneOpenFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "waveInOpen error {0}")]
    public void MicrophoneOpenFailed(uint mmsys_err)
    {
        if (IsEnabled()) WriteEvent(EvtMicrophoneOpenFailed, mmsys_err);
    }

    [Event(EvtMicrophoneTelemetryEmpty,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "Mic telemetry: no RMS samples captured (recording too short or audio thread starved)")]
    public void MicrophoneTelemetryEmpty()
    {
        if (IsEnabled()) WriteEvent(EvtMicrophoneTelemetryEmpty);
    }

    // ── Structured Heartbeat: Microphone Telemetry Payload ──────────────
    //
    // Remplace TelemetryService.Microphone(MicrophoneTelemetryPayload).
    // The 14 properties of the legacy POCO record become primitive typed
    // parameters. EventSource does not accept complex types in signatures, so
    // the payload is flattened.

    [Event(EvtMicrophoneTelemetryRecorded,
           Level = EventLevel.Verbose,
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
