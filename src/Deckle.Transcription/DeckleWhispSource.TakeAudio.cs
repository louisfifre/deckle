using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Take audio recovery ─────────────────────────────────────────────
    //
    // The audio of a dictation take is written sealed to disk while it is
    // recorded, kept when the take is lost and transcribed once at the next
    // launch. The audio is personal data: no event here carries samples or
    // text, only sizes, durations and the exception type with the system's
    // message, which names at most the file path.
    //
    // Writing: a block is written about every second, so a failing disk is one
    // incident: the first failure warns, repeats stay Verbose, the next
    // successful block closes it. Recovery: work_id pairs the start with its
    // end, whichever way it ends.

    [Event(EvtTakeAudioWriteFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Saving the take's audio failed, the dictation goes on without that part of it\nex_type={0} | ex_message={1}")]
    public void TakeAudioWriteFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtTakeAudioWriteFailed, ex_type, ex_message);
    }

    [Event(EvtTakeAudioWriteRetryFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "ex_type={0} | ex_message={1}")]
    public void TakeAudioWriteRetryFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtTakeAudioWriteRetryFailed, ex_type, ex_message);
    }

    [Event(EvtTakeAudioWriteRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "The take's audio is saved again")]
    public void TakeAudioWriteRecovered(int failures)
    {
        if (IsEnabled()) WriteEvent(EvtTakeAudioWriteRecovered, failures);
    }

    // The caller reports the Unreadable outcome; this keeps why.
    [Event(EvtTakeAudioReadFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "ex_type={0} | ex_message={1}")]
    public void TakeAudioReadFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtTakeAudioReadFailed, ex_type, ex_message);
    }

    [Event(EvtTakeAudioDeleteFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A take's audio file could not be deleted, so it may be transcribed at the next launch\nex_type={0} | ex_message={1}")]
    public void TakeAudioDeleteFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtTakeAudioDeleteFailed, ex_type, ex_message);
    }

    // The take ended without delivering its text, so its audio stays for the
    // next launch. outcome and reason are the take's own end (TakeOutcomes and
    // TakeEndReasons); audio_sec is what reached the disk, bytes the file size.
    [Event(EvtTakeAudioKept,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "The take's audio is kept for recovery at the next launch")]
    public void TakeAudioKept(string outcome, string reason, double audio_sec, long bytes)
    {
        if (IsEnabled()) WriteEvent(EvtTakeAudioKept, outcome, reason, audio_sec, bytes);
    }

    // records is how many sealed blocks were read, tail_lost whether bytes after
    // the last whole one were dropped (a crash cuts the last record).
    [Event(EvtTakeAudioRecoveryStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "Recovering an interrupted take from its audio")]
    public void TakeAudioRecoveryStarted(string work_id, double audio_sec, int records, bool tail_lost)
    {
        if (IsEnabled()) WriteEvent(EvtTakeAudioRecoveryStarted, work_id, audio_sec, records, tail_lost);
    }

    // The text reached the clipboard; chars is its length, never the text.
    [Event(EvtTakeAudioRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "The interrupted take was recovered from its audio")]
    public void TakeAudioRecovered(string work_id, int chars, double audio_sec, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtTakeAudioRecovered, work_id, chars, audio_sec, duration_ms);
    }

    // reason: no_text (the audio held no speech), engine_busy, transcription_failed
    // or exception (ex_type then names it, empty otherwise; the engine's own
    // failures carry their cause on the take's end). kept=true means the audio
    // waits for the next launch; the text copy, when there is one, is restored
    // in its place.
    [Event(EvtTakeAudioRecoveryFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The interrupted take could not be recovered from its audio\nwork_id={0} | reason={1} | ex_type={2} | kept={3} | duration_ms={4}")]
    public void TakeAudioRecoveryFailed(string work_id, string reason, string ex_type, bool kept, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtTakeAudioRecoveryFailed, work_id, reason, ex_type, kept, duration_ms);
    }

    // No recovery ran. reason: previous_attempt_crashed (an ".attempted" file
    // is left by a run that never finished: the audio is kept and the app does
    // not retry), attempt_marker_failed (the file could not be set aside, so a
    // crash could not be told apart later), unreadable (the file goes back for
    // the next launch, since a transient read error looks the same) or empty
    // (the file is discarded). kept=false means the audio is gone.
    [Event(EvtTakeAudioRecoverySkipped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The interrupted take's audio was not transcribed\nreason={0} | kept={1}")]
    public void TakeAudioRecoverySkipped(string reason, bool kept)
    {
        if (IsEnabled()) WriteEvent(EvtTakeAudioRecoverySkipped, reason, kept);
    }
}
