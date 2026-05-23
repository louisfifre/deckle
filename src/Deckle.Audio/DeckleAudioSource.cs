using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Audio;

// Audio module provider. Couvre la capture microphone (boucle waveIn,
// détection low-audio, capture lag, cap de durée), le récap télémétrie
// microphone par recording (payload structuré à 14 champs), et la
// persistance settings du module.
//
// Singleton statique, instanciation thread-safe via `static readonly`.
// Le manifest ETW est self-describing (hérité de DeckleEventSource) ;
// les noms des paramètres en snake_case deviennent directement les
// clés JSON dans le payload émis par JsonlEventListener.
//
// Conventions de naming pour ce provider :
//   - Jalons concis (Capital, niveau Informational) → suffixe verbe
//     au passé : RecordingStarted, RecordingCompleted.
//   - Verbose miroirs structurés → suffixe -Started / -Completed
//     en miroir : CaptureStarted, CaptureCompleted.
//   - Payload structuré (heartbeat) → suffixe -Recorded :
//     MicrophoneTelemetryRecorded.
//   - Anomalies → nom de la condition au passé :
//     EmptyBufferReceived, LowAudioDetected, CaptureLagDetected,
//     DurationCapReached, MicrophoneOpenFailed,
//     MicrophoneTelemetryEmpty.
//   - Persistance settings du module → préfixe Settings- :
//     SettingsLoaded, SettingsLoadComplete, SettingsLoadWarning,
//     SettingsLoadError.
[EventSource(Name = "Deckle.Audio")]
public sealed class DeckleAudioSource : DeckleEventSource
{
    public static readonly DeckleAudioSource Log = new();

    private DeckleAudioSource() { }

    // ── EventIds — séquentiels à partir de 1, jamais réutilisés ─────────
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

    // ── Recording lifecycle (jalons + verbose miroirs) ──────────────────

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

    // ── Anomalies captées dans la boucle waveIn ─────────────────────────

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

    [Event(EvtCaptureLagDetected,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "capture lag | buffers_ready={0} iter={1} wait_ms={2} prev_iter_ms={3} gc0={4}->{5} gc1={6}->{7} gc2={8}->{9}")]
    public void CaptureLagDetected(int buffers_ready, long iter, long wait_ms, long prev_iter_ms, int gc0_start, int gc0_now, int gc1_start, int gc1_now, int gc2_start, int gc2_now)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLagDetected, buffers_ready, iter, wait_ms, prev_iter_ms, gc0_start, gc0_now, gc1_start, gc1_now, gc2_start, gc2_now);
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

    // ── Heartbeat structuré — payload télémétrie microphone ─────────────
    //
    // Remplace TelemetryService.Microphone(MicrophoneTelemetryPayload).
    // Les 14 propriétés du POCO record legacy deviennent paramètres
    // typés primitifs. EventSource n'accepte pas de types complexes
    // en signature, c'est pour ça que le payload est aplati.

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

    // ── Persistance settings du module ──────────────────────────────────
    //
    // Le pattern legacy faisait passer ces lignes par LogSource.Settings
    // avec un préfixe "[audio]" inscrit dans le message. La nouvelle
    // architecture les rapatrie au provider qui les émet (DeckleAudioSource
    // est dans Deckle.Audio, le SettingsService aussi) — la source label
    // devient AUDIO via le bridge LogWindow, plus SETTINGS. Le préfixe
    // dans le message disparaît parce que le tag fait déjà le travail.
    //
    // Entorse à la doctrine « strict-typed per opération ». Les delegates
    // de JsonSettingsStore<T> dans Deckle.Core sont Action<string> et
    // appellent ces 4 méthodes avec un message paramétré ; je ne sais
    // pas, au site d'appel du delegate, distinguer « Settings loaded »
    // de « Settings initialized (defaults) » ou de « reloaded from disk
    // ». Le typage de cette zone est donc par niveau et par keyword,
    // pas par opération. La refonte propre vient à la vague 4 quand
    // SettingsHost / JsonSettingsStore basculent eux-mêmes sur un
    // contrat EventSource direct, et ces 4 events seront alors
    // remplacés par leurs équivalents per-opération.

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
