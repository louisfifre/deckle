using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Speech;

// Speech (read-aloud / TTS) module provider. Covers the read-aloud gesture
// (clipboard → synthesis → speaker output), the backend synthesis path, and
// module settings persistence.
//
// Provider Name = "Deckle-Speech": the LogWindow tag reads [SPEECH]. Mirrors
// the DeckleAmbientSource / DeckleAudioSource shape — milestones at
// Informational, structured detail at Verbose. snake_case [Event] parameter
// names become JSON keys in the JSONL payload.
[EventSource(Name = "Deckle-Speech")]
public sealed class DeckleSpeechSource : DeckleEventSource
{
    public static readonly DeckleSpeechSource Log = new();

    private DeckleSpeechSource() { }

    // ── EventIds: sequential from 1, never reused ───────────────────────
    public const int EvtReadAloudRequested      = 1;
    public const int EvtReadAloudRequestedDetail = 2;
    public const int EvtReadAloudClipboardEmpty = 3;
    public const int EvtStubSynthesis           = 4;
    public const int EvtSynthesisFailed         = 5;
    public const int EvtSynthesisFailedDetail   = 6;
    public const int EvtPlaybackFailed          = 7;
    public const int EvtPlaybackFailedDetail    = 8;
    public const int EvtSettingsLoaded          = 9;
    public const int EvtSettingsLoadComplete    = 10;
    public const int EvtSettingsLoadWarning     = 11;
    public const int EvtSettingsLoadError       = 12;
    public const int EvtReadAloudComplete       = 13;
    public const int EvtReadAloudCompleteDetail = 14;

    // ── Read-aloud gesture ──────────────────────────────────────────────

    [Event(EvtReadAloudRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Read aloud requested")]
    public void ReadAloudRequested()
    {
        if (IsEnabled()) WriteEvent(EvtReadAloudRequested);
    }

    [Event(EvtReadAloudRequestedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "read aloud | chars={0} | voice={1} | temperature={2:F2}")]
    public void ReadAloudRequestedDetail(int chars, string voice, double temperature)
    {
        if (IsEnabled()) WriteEvent(EvtReadAloudRequestedDetail, chars, voice, temperature);
    }

    [Event(EvtReadAloudClipboardEmpty,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Read aloud — the clipboard holds no text to speak")]
    public void ReadAloudClipboardEmpty()
    {
        if (IsEnabled()) WriteEvent(EvtReadAloudClipboardEmpty);
    }

    // ── Synthesis ───────────────────────────────────────────────────────

    // Skeleton-only: the placeholder backend emits a tone instead of speech.
    // Drops out when the real Chatterbox ONNX decode lands.
    [Event(EvtStubSynthesis,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "synthesis | backend=stub | placeholder_tone")]
    public void StubSynthesis()
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtStubSynthesis);
    }

    [Event(EvtSynthesisFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Speech synthesis failed")]
    public void SynthesisFailed()
    {
        if (IsEnabled()) WriteEvent(EvtSynthesisFailed);
    }

    [Event(EvtSynthesisFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "synthesis failed | ex_type={0} | ex_message={1}")]
    public void SynthesisFailedDetail(string ex_type, string ex_message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtSynthesisFailedDetail, ex_type, ex_message);
    }

    // ── Playback ────────────────────────────────────────────────────────

    [Event(EvtPlaybackFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Audio playback failed")]
    public void PlaybackFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPlaybackFailed);
    }

    [Event(EvtPlaybackFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "playback failed | ex_type={0} | ex_message={1}")]
    public void PlaybackFailedDetail(string ex_type, string ex_message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPlaybackFailedDetail, ex_type, ex_message);
    }

    // ── Completion ──────────────────────────────────────────────────────
    // Terminal milestone closing the read-aloud bracket: a clip was synthesized
    // and played to the speaker without interruption. ReadAloudRequested →
    // ReadAloudComplete makes the happy path legible at Informational level —
    // the user sees the flow ran AND finished, not just that it started. The
    // sibling transcription path brackets its takes the same way.

    [Event(EvtReadAloudComplete,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Read aloud finished")]
    public void ReadAloudComplete()
    {
        if (IsEnabled()) WriteEvent(EvtReadAloudComplete);
    }

    [Event(EvtReadAloudCompleteDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "read aloud finished | samples={0} | duration_ms={1} | sample_rate={2}")]
    public void ReadAloudCompleteDetail(int samples, long duration_ms, int sample_rate)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtReadAloudCompleteDetail, samples, duration_ms, sample_rate);
    }

    // ── Module settings persistence ─────────────────────────────────────
    // Same shape as DeckleAudioSource: the JsonSettingsStore<T> delegates are
    // Action<string>, so these are typed by level/keyword rather than per
    // operation. The caller prefixes "[speech]".

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
