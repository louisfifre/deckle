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
           Message = "GlobalAlloc failed | bytes={0}")]
    public void ClipboardAllocFailed(int bytes)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardAllocFailed, bytes);
    }

    [Event(EvtClipboardOpen,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "OpenClipboard | ok={0}")]
    public void ClipboardOpen(bool ok)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardOpen, ok);
    }

    [Event(EvtClipboardOpenFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "OpenClipboard failed")]
    public void ClipboardOpenFailed()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardOpenFailed);
    }

    [Event(EvtClipboardSetDataFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "SetClipboardData failed | handle=0")]
    public void ClipboardSetDataFailed()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardSetDataFailed);
    }

    [Event(EvtClipboardVerifyMissing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "verify failed | reason=no_unicode_data")]
    public void ClipboardVerifyMissing()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardVerifyMissing);
    }

    [Event(EvtClipboardVerifyMismatch,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "verify failed | expected_chars={0} | actual_chars={1}")]
    public void ClipboardVerifyMismatch(int expected_chars, int actual_chars)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardVerifyMismatch, expected_chars, actual_chars);
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

    [Event(EvtPasteSkippedNoForeground,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "skipped: no foreground window. Clipboard holds the text — Ctrl+V where you want it.")]
    public void PasteSkippedNoForeground()
    {
        if (IsEnabled()) WriteEvent(EvtPasteSkippedNoForeground);
    }

    [Event(EvtPasteSkippedSelfTarget,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "skipped: foreground is Deckle itself. Clipboard holds the text — Ctrl+V in the right window.")]
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

    [Event(EvtPasteSkippedNotTextField,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "skipped: focused element is not a text field. Clipboard holds the text — Ctrl+V where you want it.")]
    public void PasteSkippedNotTextField()
    {
        if (IsEnabled()) WriteEvent(EvtPasteSkippedNotTextField);
    }

    [Event(EvtPasteSendInputPartial,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "partial: SendInput injected {0}/{1} events. Clipboard holds the text — Ctrl+V manually.")]
    public void PasteSendInputPartial(int sent, int total)
    {
        if (IsEnabled()) WriteEvent(EvtPasteSendInputPartial, sent, total);
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