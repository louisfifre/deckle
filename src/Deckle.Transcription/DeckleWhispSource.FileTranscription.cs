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
    // lifecycle, paths and content-free counts.

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
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
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
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtFileTranscriptionBatchCompletedDetail, files, outcome);
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
           Message = "file transcription start | path={0} | bytes={1}")]
    public void FileTranscriptionStartedDetail(string path, long bytes)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtFileTranscriptionStartedDetail, path, bytes);
    }

    [Event(EvtFileTranscriptionSaved,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Transcript saved to file")]
    public void FileTranscriptionSaved()
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtFileTranscriptionSaved);
    }

    [Event(EvtFileTranscriptionSavedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "file transcription saved | path={0} | chars={1}")]
    public void FileTranscriptionSavedDetail(string path, int chars)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtFileTranscriptionSavedDetail, path, chars);
    }

    // Verbose, like HotkeyToggleIgnored: a file request refused because the engine
    // was busy. The user already saw the busy overlay; this is the diagnostic line.
    [Event(EvtFileTranscriptionIgnored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "file transcription ignored | state={0}")]
    public void FileTranscriptionIgnored(string state)
    {
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionIgnored, state);
    }

    [Event(EvtFileDecodeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The audio file could not be decoded")]
    public void FileDecodeFailed()
    {
        if (IsEnabled()) WriteEvent(EvtFileDecodeFailed);
    }

    [Event(EvtFileDecodeFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "file decode failed | status={0}")]
    public void FileDecodeFailedDetail(string status)
    {
        if (IsEnabled()) WriteEvent(EvtFileDecodeFailedDetail, status);
    }

    [Event(EvtFileTranscriptionWriteFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Saving the transcript to a file failed")]
    public void FileTranscriptionWriteFailed()
    {
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionWriteFailed);
    }

    [Event(EvtFileTranscriptionWriteFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "file transcription write failed | ex_type={0} | ex_message={1}")]
    public void FileTranscriptionWriteFailedDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionWriteFailedDetail, ex_type, ex_message);
    }
}
