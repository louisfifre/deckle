using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── File transcription ──────────────────────────────────────────────
    //
    // The tray-driven "transcribe a file" path: decode → single backend call →
    // .txt on disk. Milestones a human follows in the LogWindow, each paired with
    // a Verbose mirror carrying the path / size / status. The Media-Foundation
    // decode detail is logged by the Audio provider — these engine-side events
    // reference only what the engine itself decides.

    [Event(EvtFileTranscriptionStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Transcribing a file")]
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
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionStartedDetail, path, bytes);
    }

    [Event(EvtFileTranscriptionSaved,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Transcript saved to file")]
    public void FileTranscriptionSaved()
    {
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionSaved);
    }

    [Event(EvtFileTranscriptionSavedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "file transcription saved | path={0} | chars={1}")]
    public void FileTranscriptionSavedDetail(string path, int chars)
    {
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionSavedDetail, path, chars);
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
