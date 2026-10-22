using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Speech;

// Speech (read-aloud / TTS) module provider: the engine lifecycle, the
// read-aloud request, the backend synthesis path and module settings
// persistence.
[EventSource(Name = "Deckle-Speech")]
public sealed class DeckleSpeechSource : DeckleEventSource
{
    public static readonly DeckleSpeechSource Log = new();

    private DeckleSpeechSource() { }

    // ── EventIds: sequential from 1, never reused ───────────────────────
    public const int EvtReadAloudRequested      = 1;
    public const int EvtReadAloudRequestedDetail = 2;
    // 3 retired.
    public const int EvtStubSynthesis           = 4;
    // 5 retired.
    // 6 retired.
    // 7 retired.
    // 8 retired.
    public const int EvtSettingsLoaded          = 9;
    public const int EvtSettingsLoadComplete    = 10;
    public const int EvtSettingsLoadWarning     = 11;
    public const int EvtSettingsLoadError       = 12;
    // 13 retired.
    public const int EvtReadAloudCompleteDetail = 14;
    public const int EvtSpeechSynthesisFailed   = 15;
    public const int EvtSpeechPlaybackFailed    = 16;
    public const int EvtReadAloudEnded          = 17;
    public const int EvtSpeechEngineStarted     = 18;
    public const int EvtSpeechEngineStopped     = 19;

    // ── Engine lifecycle ────────────────────────────────────────────────

    // Names the backend serving read-aloud, so a reader learns from the journal
    // alone whether speech is real or the placeholder tone (accelerator=stub).
    [Event(EvtSpeechEngineStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Speech engine started | backend={0} | accelerator={1}")]
    public void SpeechEngineStarted(string backend, string accelerator)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechEngineStarted, backend, accelerator);
    }

    [Event(EvtSpeechEngineStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Speech engine stopped")]
    public void SpeechEngineStopped()
    {
        if (IsEnabled()) WriteEvent(EvtSpeechEngineStopped);
    }

    // ── Read-aloud gesture ──────────────────────────────────────────────

    [Event(EvtReadAloudRequested,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Journal,
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

    [Event(EvtSpeechSynthesisFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Speech synthesis failed | ex_type={0} | ex_message={1}")]
    public void SpeechSynthesisFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechSynthesisFailed, ex_type, ex_message);
    }

    // ── Playback ────────────────────────────────────────────────────────

    // reason is exception (Play threw: ex_type and ex_message say why) or
    // device_unavailable (Play returned false: SpeakerOutput logged the
    // mmsys_err under the audio provider, and ex_type stays empty).
    [Event(EvtSpeechPlaybackFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Audio playback failed | reason={0} | ex_type={1} | ex_message={2}")]
    public void SpeechPlaybackFailed(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSpeechPlaybackFailed, reason, ex_type, ex_message);
    }

    // ── Completion ──────────────────────────────────────────────────────
    // Terminal milestone closing the bracket ReadAloudRequested opened, on every
    // exit path. outcome is completed, cancelled or failed; reason is played,
    // interrupted (a newer request), disposed, synthesis_failed,
    // playback_failed, no_audio or unexpected.

    [Event(EvtReadAloudEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Journal,
           Message = "Read aloud ended | outcome={0} | reason={1} | elapsed_ms={2}")]
    public void ReadAloudEnded(string outcome, string reason, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtReadAloudEnded, outcome, reason, elapsed_ms);
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
    // The JsonSettingsStore<T> delegates are Action<string>, so these are typed
    // by level rather than per operation; the store's text already carries the
    // operation and its cause.

    [Event(EvtSettingsLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Speech settings | {0}")]
    public void SettingsLoaded(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoaded, message);
    }

    [Event(EvtSettingsLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "speech settings | {0}")]
    public void SettingsLoadComplete(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadComplete, PathRedaction.Redact(message));
    }

    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Speech settings warning | {0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Speech settings error | {0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }
}
