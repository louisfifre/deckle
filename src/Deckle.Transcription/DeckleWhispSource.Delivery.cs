using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Clipboard ───────────────────────────────────────────────────────
    //
    // One line per copy, the one that says how it ended. The allocation and
    // the opening each had a line of their own (events 54 and 56, retired):
    // their bytes and handle now ride on the ending line, and ok=True is what
    // reaching ClipboardCopyComplete already means. h_mem is the GlobalAlloc
    // handle, printed in hex as Windows tools show handles; 0 on an
    // allocation failure tells it from a lock failure, which got a handle.

    [Event(EvtClipboardAllocFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Clipboard memory could not be allocated, so the text was not copied")]
    public void ClipboardAllocFailed()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardAllocFailed);
    }

    [Event(EvtClipboardAllocFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "bytes={0} | h_mem=0x{1:X}")]
    public void ClipboardAllocFailedDetail(int bytes, long h_mem)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardAllocFailedDetail, bytes, h_mem);
    }

    [Event(EvtClipboardOpenFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "The clipboard could not be opened, so the text was not copied\nbytes={0}")]
    public void ClipboardOpenFailed(int bytes)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardOpenFailed, bytes);
    }

    [Event(EvtClipboardSetDataFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "The text could not be written to the clipboard, so it was not copied\nbytes={0}")]
    public void ClipboardSetDataFailed(int bytes)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardSetDataFailed, bytes);
    }

    [Event(EvtClipboardVerifyMissing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "The clipboard held no text after the copy, so the paste may be empty")]
    public void ClipboardVerifyMissing()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardVerifyMissing);
    }

    [Event(EvtClipboardVerifyMismatch,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "The clipboard held other text after the copy, so the paste may be wrong")]
    public void ClipboardVerifyMismatch()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardVerifyMismatch);
    }

    [Event(EvtClipboardVerifyMismatchDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "expected_chars={0} | actual_chars={1}")]
    public void ClipboardVerifyMismatchDetail(int expected_chars, int actual_chars)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardVerifyMismatchDetail, expected_chars, actual_chars);
    }

    [Event(EvtClipboardCopied,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "The transcript was copied to the clipboard")]
    public void ClipboardCopied()
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtClipboardCopied);
    }

    [Event(EvtClipboardCopyComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "chars={0,6} | bytes={1,7} | h_mem=0x{2:X}")]
    public void ClipboardCopyComplete(int chars, int bytes, long h_mem)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardCopyComplete, chars, bytes, h_mem);
    }

    // ── Paste ───────────────────────────────────────────────────────────

    [Event(EvtPasteHidSync,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "The HUD is hidden before the paste")]
    public void PasteHidSync()
    {
        if (IsEnabled()) WriteEvent(EvtPasteHidSync);
    }

    [Event(EvtPasteForeground,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "foreground_descriptor={0}")]
    public void PasteForeground(string foreground_descriptor)
    {
        if (IsEnabled()) WriteEvent(EvtPasteForeground, foreground_descriptor);
    }

    [Event(EvtPasteSkippedNoForeground,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Paste skipped — no foreground window. The text is on the clipboard, press Ctrl+V where you want it.")]
    public void PasteSkippedNoForeground()
    {
        if (IsEnabled()) WriteEvent(EvtPasteSkippedNoForeground);
    }

    [Event(EvtPasteSkippedSelfTarget,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Paste skipped — Deckle was in the foreground. The text is on the clipboard, press Ctrl+V in the right window.")]
    public void PasteSkippedSelfTarget()
    {
        if (IsEnabled()) WriteEvent(EvtPasteSkippedSelfTarget);
    }

    [Event(EvtPasteUiaDiag,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "uia_diag={0}")]
    public void PasteUiaDiag(string uia_diag)
    {
        if (IsEnabled()) WriteEvent(EvtPasteUiaDiag, uia_diag);
    }

    [Event(EvtPasteSkippedNotTextField,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Paste skipped — the focused element is not a text field. The text is on the clipboard, press Ctrl+V where you want it.")]
    public void PasteSkippedNotTextField()
    {
        if (IsEnabled()) WriteEvent(EvtPasteSkippedNotTextField);
    }

    [Event(EvtPasteSendInputPartial,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Paste was only partly sent. The text is on the clipboard, press Ctrl+V manually.")]
    public void PasteSendInputPartial()
    {
        if (IsEnabled()) WriteEvent(EvtPasteSendInputPartial);
    }

    [Event(EvtPasteSendInputPartialDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "sent={0} | total={1}")]
    public void PasteSendInputPartialDetail(int sent, int total)
    {
        if (IsEnabled()) WriteEvent(EvtPasteSendInputPartialDetail, sent, total);
    }

    [Event(EvtPasteSucceeded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Pasted")]
    public void PasteSucceeded()
    {
        if (IsEnabled()) WriteEvent(EvtPasteSucceeded);
    }

    [Event(EvtPasteSent,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "foreground_descriptor={0}")]
    public void PasteSent(string foreground_descriptor)
    {
        if (IsEnabled()) WriteEvent(EvtPasteSent, foreground_descriptor);
    }
}
