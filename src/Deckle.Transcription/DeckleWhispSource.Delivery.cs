using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Clipboard ───────────────────────────────────────────────────────

    [Event(EvtClipboardGlobalAlloc,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "GlobalAlloc | bytes={0} | hMem={1}")]
    public void ClipboardGlobalAlloc(int bytes, long h_mem)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardGlobalAlloc, bytes, h_mem);
    }

    [Event(EvtClipboardAllocFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Allocating clipboard memory failed")]
    public void ClipboardAllocFailed()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardAllocFailed);
    }

    [Event(EvtClipboardAllocFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "global alloc failed | bytes={0}")]
    public void ClipboardAllocFailedDetail(int bytes)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardAllocFailedDetail, bytes);
    }

    [Event(EvtClipboardOpen,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "OpenClipboard | ok={0}")]
    public void ClipboardOpen(bool ok)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardOpen, ok);
    }

    // In-place clean (no params, no placeholders): the Win32 API name was
    // dropped for a human sentence.
    [Event(EvtClipboardOpenFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Opening the clipboard failed")]
    public void ClipboardOpenFailed()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardOpenFailed);
    }

    // Constant-only detail dropped: the method takes no args (handle was the
    // compile-time constant 0). No Verbose mirror.
    [Event(EvtClipboardSetDataFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Writing to the clipboard failed")]
    public void ClipboardSetDataFailed()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardSetDataFailed);
    }

    // Constant-only detail dropped: the method takes no args (reason was the
    // compile-time constant no_unicode_data). No Verbose mirror.
    [Event(EvtClipboardVerifyMissing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Clipboard verification found no text")]
    public void ClipboardVerifyMissing()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardVerifyMissing);
    }

    [Event(EvtClipboardVerifyMismatch,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Clipboard verification found the wrong text")]
    public void ClipboardVerifyMismatch()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardVerifyMismatch);
    }

    [Event(EvtClipboardVerifyMismatchDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "verify mismatch | expected_chars={0} | actual_chars={1}")]
    public void ClipboardVerifyMismatchDetail(int expected_chars, int actual_chars)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardVerifyMismatchDetail, expected_chars, actual_chars);
    }

    [Event(EvtClipboardCopied,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Copied to clipboard")]
    public void ClipboardCopied()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardCopied);
    }

    [Event(EvtClipboardCopyComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "copy complete | chars={0} | bytes={1}")]
    public void ClipboardCopyComplete(int chars, int bytes)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardCopyComplete, chars, bytes);
    }

    // ── Paste ───────────────────────────────────────────────────────────

    [Event(EvtPasteHidSync,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "HUD hidden (HideSync) — ready to paste")]
    public void PasteHidSync()
    {
        if (IsEnabled()) WriteEvent(EvtPasteHidSync);
    }

    [Event(EvtPasteForeground,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "foreground at paste: {0}")]
    public void PasteForeground(string foreground_descriptor)
    {
        if (IsEnabled()) WriteEvent(EvtPasteForeground, foreground_descriptor);
    }

    // In-place clean (entirely human guidance, no params, no placeholders):
    // only the "skipped:" implementation prefix was dropped.
    [Event(EvtPasteSkippedNoForeground,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Paste skipped — no foreground window. The text is on the clipboard, press Ctrl+V where you want it.")]
    public void PasteSkippedNoForeground()
    {
        if (IsEnabled()) WriteEvent(EvtPasteSkippedNoForeground);
    }

    // In-place clean (entirely human guidance, no params, no placeholders):
    // only the "skipped:" implementation prefix was dropped. "Deckle" is kept —
    // it is the app name addressed to the user, not the EventSource tag.
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
           Message = "UIA: {0}")]
    public void PasteUiaDiag(string uia_diag)
    {
        if (IsEnabled()) WriteEvent(EvtPasteUiaDiag, uia_diag);
    }

    // In-place clean (entirely human guidance, no params, no placeholders):
    // only the "skipped:" implementation prefix was dropped.
    [Event(EvtPasteSkippedNotTextField,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Paste skipped — the focused element is not a text field. The text is on the clipboard, press Ctrl+V where you want it.")]
    public void PasteSkippedNotTextField()
    {
        if (IsEnabled()) WriteEvent(EvtPasteSkippedNotTextField);
    }

    // User-facing guidance kept as the milestone; the injected/total counts
    // move to the Verbose mirror.
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
           Message = "send input partial | sent={0} | total={1}")]
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
           Message = "Ctrl+V sent to {0}")]
    public void PasteSent(string foreground_descriptor)
    {
        if (IsEnabled()) WriteEvent(EvtPasteSent, foreground_descriptor);
    }
}