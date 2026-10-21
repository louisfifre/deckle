using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Vad;

// EventSource provider for the VAD module — the external speech-activity detector
// that trims each streaming utterance to its speech and drops a no-speech one.
// Same Pipeline keyword as the rest of the staged pipeline.
[EventSource(Name = "Deckle-Vad")]
public sealed class DeckleVadSource : DeckleEventSource
{
    public static readonly DeckleVadSource Log = new();

    private DeckleVadSource() { }

    // Per-take and per-utterance detail belongs to the transcription workflow
    // while a take runs, so its verbosity control governs it; outside a take the
    // provider's own diagnostics stay unaffected. One-shot load, download and
    // incident mirrors are not governed: they tell how the model came up.
    private bool IsTrimDetailEnabled()
        => OperationalLogAdmission.IsScopedDetailEnabled(
            OperationalLogActivity.Transcription, this, EventLevel.Verbose, (EventKeywords)Keywords.Pipeline);

    // EventIds: sequential from 1, never reused; a new event takes the next id.
    public const int EvtSpeechTrimmed                       = 1;
    public const int EvtUtteranceDroppedNoSpeech            = 2;
    public const int EvtSpeechTrimVadLoaded                 = 3;
    // 4 retired.
    public const int EvtSpeechTrimVadDownloadStart          = 5;
    public const int EvtSpeechTrimVadDownloadComplete       = 6;
    public const int EvtSpeechTrimNotReady                  = 7;
    public const int EvtSpeechTrimSettingsSnapshot          = 8;
    public const int EvtSpeechTrimVadLoadedDetail           = 9;
    // 10 retired.
    public const int EvtSpeechTrimVadDownloadStartDetail    = 11;
    public const int EvtSpeechTrimVadDownloadCompleteDetail = 12;
    public const int EvtSpeechTrimVadLoadComplete           = 13;
    public const int EvtSpeechTrimUnavailable               = 14;
    public const int EvtSpeechTrimUnavailableDetail         = 15;
    public const int EvtSpeechTrimRecovered                 = 16;
    public const int EvtSpeechTrimVadFileDeleteFailed       = 17;

    [Event(EvtSpeechTrimmed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "speech trimmed | utterance_index={0} | in_samples={1} | out_samples={2} | speech_segments={3} | trim_ms={4}")]
    public void SpeechTrimmed(int utterance_index, int in_samples, int out_samples, int speech_segments, long trim_ms)
    {
        if (IsTrimDetailEnabled()) WriteEvent(EvtSpeechTrimmed, utterance_index, in_samples, out_samples, speech_segments, trim_ms);
    }

    // Logged before the trim activity, so a reread of the log shows which
    // threshold and durations were active for the take.
    [Event(EvtSpeechTrimSettingsSnapshot,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "speech trim settings | threshold={0:F2} | min_speech_ms={1} | min_silence_ms={2} | speech_pad_ms={3}")]
    public void SpeechTrimSettingsSnapshot(double threshold, int min_speech_ms, int min_silence_ms, int speech_pad_ms)
    {
        if (IsTrimDetailEnabled()) WriteEvent(EvtSpeechTrimSettingsSnapshot, threshold, min_speech_ms, min_silence_ms, speech_pad_ms);
    }

    [Event(EvtUtteranceDroppedNoSpeech,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "utterance dropped, no speech detected | utterance_index={0}")]
    public void UtteranceDroppedNoSpeech(int utterance_index)
    {
        if (IsTrimDetailEnabled()) WriteEvent(EvtUtteranceDroppedNoSpeech, utterance_index);
    }

    [Event(EvtSpeechTrimVadLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Silero VAD loaded")]
    public void SpeechTrimVadLoaded()
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadLoaded);
    }

    [Event(EvtSpeechTrimVadLoadedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "model loaded | model_path={0}")]
    public void SpeechTrimVadLoadedDetail(string model_path)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadLoadedDetail, model_path);
    }

    // Wall-clock for the VAD load: SHA-256 verify + ONNX session construction,
    // measured together in VadService. A separate event because
    // SpeechTrimVadLoadedDetail's signature is frozen.
    [Event(EvtSpeechTrimVadLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "load complete | load_ms={0}")]
    public void SpeechTrimVadLoadComplete(long load_ms)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadLoadComplete, load_ms);
    }

    // Opens the "takes run untrimmed" incident, once per cause: a later take that
    // finds the VAD unusable for the same reason adds nothing. reason is one of
    // model_missing, checksum_mismatch, checksum_read_failed, load_failed,
    // download_failed, trim_failed. ex_type is empty when no exception was
    // thrown; ex_message then carries the failure text when there is one.
    [Event(EvtSpeechTrimUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Speech trim is off, takes run untrimmed | reason={0} | ex_type={1} | ex_message={2}")]
    public void SpeechTrimUnavailable(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimUnavailable, reason, ex_type, ex_message);
    }

    [Event(EvtSpeechTrimUnavailableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "speech trim unavailable | reason={0} | model_path={1}")]
    public void SpeechTrimUnavailableDetail(string reason, string model_path)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimUnavailableDetail, reason, model_path);
    }

    // Closes the incident SpeechTrimUnavailable opened, so the journal never
    // shows a degradation without its end.
    [Event(EvtSpeechTrimRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Speech trim is back on | previous_reason={0}")]
    public void SpeechTrimRecovered(string previous_reason)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimRecovered, previous_reason);
    }

    // A stale or corrupt model that cannot be deleted blocks the re-download;
    // the download failure then carries the visible cause.
    [Event(EvtSpeechTrimVadFileDeleteFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "model delete failed | model_path={0} | ex_type={1} | ex_message={2}")]
    public void SpeechTrimVadFileDeleteFailed(string model_path, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadFileDeleteFailed, model_path, ex_type, ex_message);
    }

    [Event(EvtSpeechTrimVadDownloadStart,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Downloading the Silero VAD model")]
    public void SpeechTrimVadDownloadStart()
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadDownloadStart);
    }

    [Event(EvtSpeechTrimVadDownloadStartDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "model download started | url={0}")]
    public void SpeechTrimVadDownloadStartDetail(string url)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadDownloadStartDetail, url);
    }

    [Event(EvtSpeechTrimVadDownloadComplete,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Silero VAD model downloaded")]
    public void SpeechTrimVadDownloadComplete()
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadDownloadComplete);
    }

    [Event(EvtSpeechTrimVadDownloadCompleteDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "model download complete | model_path={0}")]
    public void SpeechTrimVadDownloadCompleteDetail(string model_path)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadDownloadCompleteDetail, model_path);
    }

    // The raw per-take attempt; the visible incident is SpeechTrimUnavailable,
    // which VadService opens once per cause.
    [Event(EvtSpeechTrimNotReady,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "take runs untrimmed | reason=vad_not_ready")]
    public void SpeechTrimNotReady()
    {
        if (IsTrimDetailEnabled()) WriteEvent(EvtSpeechTrimNotReady);
    }
}
