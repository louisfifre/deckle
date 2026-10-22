using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── File transcription ──────────────────────────────────────────────
    //
    // The tray-driven path: one selection crosses the shared segmented consumer
    // and writes one adjacent .txt per usable file. The Media-Foundation decode
    // detail is logged by the Audio provider; these events carry only engine
    // lifecycle, paths and content-free counts. index is the file's 1-based
    // position in its batch and files the batch size, so a reader can follow
    // a batch file by file.

    [Event(EvtFileTranscriptionBatchStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "File transcription batch started")]
    public void FileTranscriptionBatchStarted()
    {
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionBatchStarted);
    }

    [Event(EvtFileTranscriptionBatchStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "file transcription batch start | files={0} | prepared_capacity={1}")]
    public void FileTranscriptionBatchStartedDetail(int files, int prepared_capacity)
    {
        if (!IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtFileTranscriptionBatchStartedDetail, files, prepared_capacity);
    }

    [Event(EvtFileTranscriptionBatchCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "File transcription batch completed")]
    public void FileTranscriptionBatchCompleted()
    {
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionBatchCompleted);
    }

    [Event(EvtFileTranscriptionBatchCompletedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "file transcription batch complete | files={0} | outcome={1}")]
    public void FileTranscriptionBatchCompletedDetail(int files, string outcome)
    {
        if (!IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtFileTranscriptionBatchCompletedDetail, files, outcome);
    }

    // The end of a batch that did not complete. outcome (closed vocabulary):
    // cancelled | failed. ex_type and ex_message are empty when cancelled; a
    // failure's full cause is in the PipelineCrashed that precedes it.
    [Event(EvtFileTranscriptionBatchAborted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "File transcription batch ended early | outcome={0} | files={1} | ex_type={2} | ex_message={3}")]
    public void FileTranscriptionBatchAborted(string outcome, int files, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionBatchAborted, outcome, files, ex_type, ex_message);
    }

    [Event(EvtFileTranscriptionStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "File transcription started")]
    public void FileTranscriptionStarted()
    {
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionStarted);
    }

    [Event(EvtFileTranscriptionStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "file transcription start | index={0} | files={1} | path={2} | bytes={3}")]
    public void FileTranscriptionStartedDetail(int index, int files, string path, long bytes)
    {
        if (!IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtFileTranscriptionStartedDetail, index, files, path, bytes);
    }

    // reason (closed vocabulary): model_not_ready (the prime failed or the
    // model is not loaded), incomplete (an utterance could not be delivered),
    // exception (the pipeline threw; ex_type and ex_message carry it). file is
    // the file name without its folder.
    [Event(EvtFileTranscriptionFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A file could not be transcribed | index={0} | files={1} | file={2} | reason={3} | ex_type={4} | ex_message={5}")]
    public void FileTranscriptionFailed(int index, int files, string file, string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionFailed, index, files, file, reason, ex_type, ex_message);
    }

    [Event(EvtFileTranscriptionEmpty,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A file had no audio to transcribe | index={0} | files={1} | file={2}")]
    public void FileTranscriptionEmpty(int index, int files, string file)
    {
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionEmpty, index, files, file);
    }

    // action (closed vocabulary): enqueued (a selection joined the queue),
    // started (the queue handed a batch to the worker), deferred (the engine
    // was busy, so the queue kept its batches). queued_files is the number of
    // files still waiting after the action.
    [Event(EvtFileQueueChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "file queue changed | action={0} | files={1} | queued_files={2}")]
    public void FileQueueChanged(string action, int files, int queued_files)
    {
        if (!IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtFileQueueChanged, action, files, queued_files);
    }

    [Event(EvtFileTranscriptionSaved,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Transcript saved to file")]
    public void FileTranscriptionSaved()
    {
        if (!IsTakeDetailEnabled((EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtFileTranscriptionSaved);
    }

    [Event(EvtFileTranscriptionSavedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "file transcription saved | path={0} | chars={1}")]
    public void FileTranscriptionSavedDetail(string path, int chars)
    {
        if (!IsTakeDetailEnabled((EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtFileTranscriptionSavedDetail, path, chars);
    }

    // status is the decoder's AudioFileDecodeStatus, or ReadError when the
    // decoder threw; ex_type and ex_message are empty unless it threw.
    [Event(EvtFileDecodeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The audio file could not be decoded | file={0} | status={1} | ex_type={2} | ex_message={3}")]
    public void FileDecodeFailed(string file, string status, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtFileDecodeFailed, file, status, ex_type, ex_message);
    }

    [Event(EvtFileTranscriptionWriteFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Saving the transcript to a file failed | ex_type={0} | ex_message={1}")]
    public void FileTranscriptionWriteFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionWriteFailed, ex_type, ex_message);
    }
}
