using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Take lifecycle ──────────────────────────────────────────────────
    //
    // One take is one dictation, from the moment the worker owns it to the
    // moment its outcome is known, or one file of a batch. Both events travel
    // in the log and share `work_id`, so a reader of the file alone can say
    // what became of every take, including one that left nothing behind.
    // TranscriptionEngine guarantees the pair: a take that never reaches an
    // explicit end is closed as abandoned when its worker settles.

    // kind (closed vocabulary, TakeKinds): dictation, file. transcription_id
    // is the corpus join key, so a log line leads to the corpus line and
    // the corpus audio of the same take.
    [Event(EvtTakeStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "Take started")]
    public void TakeStarted(string work_id, string kind, string transcription_id)
    {
        if (IsEnabled()) WriteEvent(EvtTakeStarted, work_id, kind, transcription_id);
    }

    // outcome and reason (closed vocabularies, TakeOutcomes and TakeEndReasons).
    // The outcome says what reached the person; the reason says why when that
    // was nothing, or what was delivered when it was something.
    [Event(EvtTakeEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "Take ended")]
    public void TakeEnded(string work_id, string outcome, string reason, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtTakeEnded, work_id, outcome, reason, duration_ms);
    }
}

// Closed `kind` vocabulary of TakeStarted.
public static class TakeKinds
{
    // A hotkey recording.
    public const string Dictation = "dictation";

    // One audio file of a tray selection.
    public const string File = "file";
}

// Closed `outcome` vocabulary of TakeEnded.
public static class TakeOutcomes
{
    // The text reached the target field.
    public const string Pasted = "pasted";

    // The text is on the clipboard, nothing was pasted.
    public const string ClipboardOnly = "clipboard_only";

    // The transcript was written beside its source audio.
    public const string SavedToFile = "saved_to_file";

    // Nothing to deliver and nothing wrong: no audio, or no speech.
    public const string None = "none";

    // Something went wrong and the take delivered nothing.
    public const string Failed = "failed";

    // The worker settled before the take declared its end.
    public const string Abandoned = "abandoned";
}

// Closed `reason` vocabulary of TakeEnded.
public static class TakeEndReasons
{
    // The text was delivered (outcome pasted, clipboard_only, saved_to_file).
    public const string Delivered = "delivered";

    // The capture ended on a microphone error.
    public const string MicrophoneError = "microphone_error";

    // The engine left its recording or stopping state under the take: it was
    // disposed.
    public const string StateLost = "state_lost";

    // No model was loaded when the take reached the backend.
    public const string ModelNotLoaded = "model_not_loaded";

    // A file could not be transcribed because the model was not ready.
    public const string ModelNotReady = "model_not_ready";

    // The capture held no audio.
    public const string EmptyAudio = "empty_audio";

    // Transcription ran and found no text.
    public const string NoText = "no_text";

    // The backend call threw or returned an error code.
    public const string TranscriptionFailed = "transcription_failed";

    // A file take could not deliver every utterance.
    public const string Incomplete = "incomplete";

    // The raw text could not be copied to the clipboard.
    public const string CopyFailed = "copy_failed";

    // The transcript file could not be written.
    public const string WriteFailed = "write_failed";

    // An exception left the pipeline.
    public const string PipelineCrashed = "pipeline_crashed";

    // The batch was cancelled while the take was running.
    public const string Cancelled = "cancelled";

    // The worker stopped without the take declaring its end.
    public const string WorkerExit = "worker_exit";
}
