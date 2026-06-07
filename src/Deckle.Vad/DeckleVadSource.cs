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

    // ── EventIds: sequential from 1, never reused ───────────────────────
    public const int EvtSpeechTrimmed                 = 1;
    public const int EvtUtteranceDroppedNoSpeech      = 2;
    public const int EvtSpeechTrimVadLoaded           = 3;
    public const int EvtSpeechTrimVadUnavailable      = 4;
    public const int EvtSpeechTrimVadDownloadStart    = 5;
    public const int EvtSpeechTrimVadDownloadComplete = 6;
    public const int EvtSpeechTrimNotReady            = 7;
    public const int EvtSpeechTrimSettingsSnapshot    = 8;

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
           Message = "Silero VAD loaded ({0})")]
    public void SpeechTrimVadLoaded(string model_path)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadLoaded, model_path);
    }

    [Event(EvtSpeechTrimVadUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Silero VAD unavailable — {0}")]
    public void SpeechTrimVadUnavailable(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadUnavailable, reason);
    }

    [Event(EvtSpeechTrimVadDownloadStart,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Downloading Silero VAD model… ({0})")]
    public void SpeechTrimVadDownloadStart(string url)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadDownloadStart, url);
    }

    [Event(EvtSpeechTrimVadDownloadComplete,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Silero VAD model downloaded ({0})")]
    public void SpeechTrimVadDownloadComplete(string model_path)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechTrimVadDownloadComplete, model_path);
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
