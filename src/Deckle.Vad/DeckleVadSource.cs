using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Vad;

// EventSource provider for the VAD module — the external speech-activity detector
// that trims each streaming utterance to its speech and drops a no-speech one.
// These events used to ride the Whisp provider; they live here now that the VAD is
// its own module. Same Pipeline keyword as the rest of the staged pipeline.
[EventSource(Name = "Deckle-Vad")]
public sealed class DeckleVadSource : DeckleEventSource
{
    public static readonly DeckleVadSource Log = new();

    private DeckleVadSource() { }

    private new bool IsEnabled(EventLevel level, EventKeywords keywords)
        => (level != EventLevel.Verbose
            || OperationalLogAdmission.AllowsScopedDetail(OperationalLogActivity.Transcription))
        && base.IsEnabled(level, keywords);

    // ── EventIds: sequential from 1, never reused ───────────────────────
    // Milestones keep their original id; the Verbose mirrors added for the
    // Verbose/Info separation take fresh ids 9-12 at the end of the sequence.
    public const int EvtSpeechTrimmed                       = 1;
    public const int EvtUtteranceDroppedNoSpeech            = 2;
    public const int EvtSpeechTrimVadLoaded                 = 3;
    public const int EvtSpeechTrimVadUnavailable            = 4;
    public const int EvtSpeechTrimVadDownloadStart          = 5;
    public const int EvtSpeechTrimVadDownloadComplete       = 6;
    public const int EvtSpeechTrimNotReady                  = 7;
    public const int EvtSpeechTrimSettingsSnapshot          = 8;
    public const int EvtSpeechTrimVadLoadedDetail           = 9;
    public const int EvtSpeechTrimVadUnavailableDetail      = 10;
    public const int EvtSpeechTrimVadDownloadStartDetail    = 11;
    public const int EvtSpeechTrimVadDownloadCompleteDetail = 12;
    public const int EvtSpeechTrimVadLoadComplete           = 13;

    [Event(EvtSpeechTrimmed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Speech trim | utt #{0} | {1} → {2} samples | {3} spans | {4} ms")]
    public void SpeechTrimmed(int utterance_index, int in_samples, int out_samples, int speech_segments, long trim_ms)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimmed, utterance_index, in_samples, out_samples, speech_segments, trim_ms);
    }

    // Per-take config line — verbose precedes the trim activity so a reread of the
    // log never has to wonder which threshold or duration was active for the take.
    [Event(EvtSpeechTrimSettingsSnapshot,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Speech trim settings | threshold={0:F2} | min-speech={1} ms | min-silence={2} ms | pad={3} ms")]
    public void SpeechTrimSettingsSnapshot(double threshold, int min_speech_ms, int min_silence_ms, int speech_pad_ms)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimSettingsSnapshot, threshold, min_speech_ms, min_silence_ms, speech_pad_ms);
    }

    [Event(EvtUtteranceDroppedNoSpeech,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Utterance #{0} dropped — no speech detected")]
    public void UtteranceDroppedNoSpeech(int utterance_index)
    {
        if (IsEnabled()) WriteEvent(EvtUtteranceDroppedNoSpeech, utterance_index);
    }

    [Event(EvtSpeechTrimVadLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
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
    // measured together in VadService. Mirrors whisper's ModelLoadComplete
    // (load_ms on a dedicated Verbose *Complete event), with the canonical ms
    // unit. A fresh event rather than a new arg on SpeechTrimVadLoadedDetail —
    // that event's signature is frozen, and whisper carries the number on its
    // own *Complete event, not on *Detail.
    [Event(EvtSpeechTrimVadLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "load complete | load_ms={0}")]
    public void SpeechTrimVadLoadComplete(long load_ms)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadLoadComplete, load_ms);
    }

    [Event(EvtSpeechTrimVadUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Silero VAD unavailable")]
    public void SpeechTrimVadUnavailable()
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadUnavailable);
    }

    [Event(EvtSpeechTrimVadUnavailableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "model unavailable | reason={0}")]
    public void SpeechTrimVadUnavailableDetail(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadUnavailableDetail, reason);
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

    [Event(EvtSpeechTrimNotReady,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "SpeechTrim enabled but the VAD model isn't ready — this take runs untrimmed")]
    public void SpeechTrimNotReady()
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimNotReady);
    }
}
