using System.Runtime.InteropServices;
using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── Finalize partial — delivery tail (finalize + clipboard + paste) and the mic-error localizer ──

    // ── Microphone error localization ──────────────────────────────────────────
    //
    // Capture stays free of any Loc.Get dependency; the engine localizes. The
    // raw MMSYSERR code goes verbatim into the Unavailable body so users can
    // quote it when reporting.
    private static (string Title, string Body) LocalizeMicError(MicErrorKind kind, uint err) => kind switch
    {
        MicErrorKind.NotDetected => (Loc.Get("MicError_NotDetected_Title"), Loc.Get("MicError_NotDetected_Body")),
        MicErrorKind.InUse       => (Loc.Get("MicError_InUse_Title"),       Loc.Get("MicError_InUse_Body")),
        _                        => (Loc.Get("MicError_Unavailable_Title"), Loc.Format("MicError_Unavailable_Body_Format", err)),
    };


    // ── Shared finalize ──────────────────────────────────────────────────────
    //
    // Producer-agnostic tail: executes the delivery command, then emits
    // telemetry and optionally raises Finished. Dictation writes the clipboard,
    // may rewrite, and may paste; imported audio writes an adjacent text file.
    // Synchronous: every delivery step blocks.
    private TranscriptionOutcome FinalizeTranscription(
        PipelineProduction production,
        TranscriptionDelivery delivery,
        bool announceCompletion = true)
    {
        string rawText = production.RawText;
        bool isFileRun = delivery.IsFile;
        double recordingDurationSec = _recordingSw?.Elapsed.TotalSeconds ?? 0;

        // Imported audio has a different delivery command, not a different
        // transcription pipeline. Its primary and only required output is the
        // adjacent text file; clipboard availability must not gate that write or
        // replace the continuous batch HUD with a clipboard error.
        if (isFileRun)
        {
            FinalizeRewrite fileRewrite = ApplyRewrite(rawText, isFileRun: true);
            FinalizeDelivery fileDelivery = DeliverTranscript(fileRewrite.Text, delivery);
            RecordPipelineMetrics(
                production,
                rawText,
                fileRewrite,
                fileDelivery,
                clipboardMs: 0,
                isFileRun: true,
                recordingDurationSec);
            if (announceCompletion)
                RaiseFinished(fileDelivery.Outcome);
            return fileDelivery.Outcome;
        }

        // The raw copy is the safety net for every later stage. A failed rewrite
        // copy therefore leaves useful text behind, and paste never sees stale data.
        // Every write lands in the Windows clipboard history, so a take writes the
        // raw text once and at most one rewrite, never per utterance.
        var clipboardStopwatch = System.Diagnostics.Stopwatch.StartNew();
        bool rawCopySucceeded = CopyToClipboard(rawText);
        clipboardStopwatch.Stop();
        if (!rawCopySucceeded)
        {
            if (announceCompletion)
                RaiseStatus(Loc.Get("Status_Ready"));
            if (announceCompletion)
                RaiseFinished(TranscriptionOutcome.None);
            return TranscriptionOutcome.None;
        }

        FinalizeRewrite rewrite = ApplyRewrite(rawText, isFileRun, production.BatchRewrite);
        FinalizeDelivery delivered = DeliverTranscript(rewrite.Text, delivery);

        RecordPipelineMetrics(
            production,
            rawText,
            rewrite,
            delivered,
            clipboardStopwatch.ElapsedMilliseconds,
            isFileRun,
            recordingDurationSec);
        if (announceCompletion)
            RaiseFinished(delivered.Outcome);
        return delivered.Outcome;
    }

    private FinalizeDelivery DeliverTranscript(string text, TranscriptionDelivery delivery)
    {
        long pasteMs = 0;
        bool pasteVerified = false;
        if (_shouldPaste)
        {
            // Hide the HUD synchronously before Ctrl+V so Deckle cannot steal
            // activation from the target selected at Stop.
            OnReadyToPaste?.Invoke();
            DeckleWhispSource.Log.PasteHidSync();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            pasteVerified = PasteFromClipboard();
            stopwatch.Stop();
            pasteMs = stopwatch.ElapsedMilliseconds;
        }

        TranscriptionOutcome outcome = delivery.IsFile
            ? WriteFileTranscript(text, delivery.SourceAudioPath!)
            : _shouldPaste && pasteVerified
                ? TranscriptionOutcome.Pasted
                : TranscriptionOutcome.ClipboardOnly;
        return new FinalizeDelivery(outcome, pasteMs, pasteVerified);
    }

    private readonly record struct FinalizeDelivery(
        TranscriptionOutcome Outcome,
        long PasteMs,
        bool PasteVerified);

    // ── File-transcription delivery ─────────────────────────────────────────────
    //
    // A write failure returns None: no fallback output was delivered. The catch
    // covers filesystem exceptions plus the invalid-path family; anything else
    // is a genuine bug and propagates.
    private TranscriptionOutcome WriteFileTranscript(string fullText, string audioPath)
    {
        try
        {
            string written = TranscriptFileWriter.Write(fullText, audioPath);
            DeckleWhispSource.Log.FileTranscriptionSaved();
            DeckleWhispSource.Log.FileTranscriptionSavedDetail(written, fullText.Length);
            return TranscriptionOutcome.SavedToFile;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException
               or ArgumentException or NotSupportedException)
        {
            DeckleWhispSource.Log.FileTranscriptionWriteFailed();
            DeckleWhispSource.Log.FileTranscriptionWriteFailedDetail(ex.GetType().Name, ex.Message);
            EmitUserFeedback(FB_WARN,
                Loc.Get("FileTranscription_WriteFailed_Title"),
                Loc.Get("FileTranscription_WriteFailed_Body"),
                FB_OVERLAY);
            return TranscriptionOutcome.None;
        }
    }

    // ── Clipboard ─────────────────────────────────────────────────────────────

    // False on any of the three fatal branches (GlobalAlloc, OpenClipboard,
    // SetClipboardData), each with an error UserFeedback. A failed read-back
    // only warns, since the bytes reached the clipboard; the length check is a
    // safety net against format mangling by a third-party clipboard watcher.
    private bool CopyToClipboard(string text)
    {
        ClipboardWriteResult r = _clipboard.TryWriteText(text);

        DeckleWhispSource.Log.ClipboardGlobalAlloc(r.ByteCount, r.Handle);

        if (r.Status == ClipboardWriteStatus.AllocFailed)
        {
            DeckleWhispSource.Log.ClipboardAllocFailed();
            DeckleWhispSource.Log.ClipboardAllocFailedDetail(r.ByteCount);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_ClipboardCopyFailed_Memory_Title"),
                Loc.Get("Engine_ClipboardCopyFailed_Memory_Body"),
                FB_REPLACEMENT);
            return false;
        }

        if (r.Status == ClipboardWriteStatus.OpenFailed)
        {
            DeckleWhispSource.Log.ClipboardOpen(false);
            DeckleWhispSource.Log.ClipboardOpenFailed();
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_ClipboardUnavailable_Title"),
                Loc.Get("Engine_ClipboardUnavailable_Body"),
                FB_REPLACEMENT);
            return false;
        }

        DeckleWhispSource.Log.ClipboardOpen(true);

        if (r.Status == ClipboardWriteStatus.SetDataFailed)
        {
            DeckleWhispSource.Log.ClipboardSetDataFailed();
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_ClipboardCopyFailed_Refused_Title"),
                Loc.Get("Engine_ClipboardCopyFailed_Refused_Body"),
                FB_REPLACEMENT);
            return false;
        }

        // The two Verify states are advisory: the read-back flagged a
        // discrepancy, but the copy landed.
        if (r.Status == ClipboardWriteStatus.VerifyMissing)
        {
            DeckleWhispSource.Log.ClipboardVerifyMissing();
            EmitUserFeedback(FB_WARN,
                Loc.Get("Engine_ClipboardIncomplete_Unverified_Title"),
                Loc.Get("Engine_ClipboardIncomplete_Unverified_Body"),
                FB_OVERLAY);
        }
        else if (r.Status == ClipboardWriteStatus.VerifyLengthMismatch)
        {
            DeckleWhispSource.Log.ClipboardVerifyMismatch();
            DeckleWhispSource.Log.ClipboardVerifyMismatchDetail(r.ExpectedChars, r.ActualChars);
            EmitUserFeedback(FB_WARN,
                Loc.Get("Engine_ClipboardIncomplete_LengthMismatch_Title"),
                Loc.Get("Engine_ClipboardIncomplete_LengthMismatch_Body"),
                FB_OVERLAY);
        }

        DeckleWhispSource.Log.ClipboardCopied();
        DeckleWhispSource.Log.ClipboardCopyComplete(r.ExpectedChars, r.ByteCount);
        return true;
    }

    // Sends Ctrl+V to whatever window has the foreground at Stop time, but only
    // when UI Automation confirms the focused element is a text-accepting
    // control (Edit or Document). No Start-time capture, no bring-to-front, no
    // focus comparison: the user had the whole recording and transcription to
    // place their cursor.
    //
    // The clipboard is the safe default. When in doubt (UIA refuses to answer,
    // unknown control type, foreground is Deckle itself) the text stays on the
    // clipboard and the HUD shows the Ctrl+V reminder.
    private bool PasteFromClipboard()
    {
        const uint   INPUT_KEYBOARD  = 1;
        const uint   KEYEVENTF_KEYUP = 0x0002;
        const ushort VK_CONTROL      = 0x11;
        const ushort VK_V            = 0x56;

        IntPtr fg = NativeMethods.GetForegroundWindow();
        DeckleWhispSource.Log.PasteForeground(Win32Util.DescribeHwnd(fg));

        if (fg == IntPtr.Zero)
        {
            DeckleWhispSource.Log.PasteSkippedNoForeground();
            return false;
        }

        // Refuse if the foreground is a Deckle window itself (LogWindow, HUD,
        // Settings). Avoids the false positive where we would paste into our
        // own logs while the user reads them.
        NativeMethods.GetWindowThreadProcessId(fg, out uint fgPid);
        uint ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        if (fgPid == ownPid)
        {
            DeckleWhispSource.Log.PasteSkippedSelfTarget();
            return false;
        }

        // UI Automation, not window class names: Chromium, Electron and Qt draw
        // their text fields inside one window, so a class name cannot see them.
        // Anything other than "Edit or Document" falls back to clipboard-only.
        bool editable = UIAutomation.IsFocusedElementTextEditable(out string uiaDiag);
        DeckleWhispSource.Log.PasteUiaDiag(uiaDiag);
        if (!editable)
        {
            DeckleWhispSource.Log.PasteSkippedNotTextField();
            return false;
        }

        int cbSize = Marshal.SizeOf<INPUT>();

        var inputs = new INPUT[]
        {
            new INPUT { type = INPUT_KEYBOARD, ki_wVk = VK_CONTROL },
            new INPUT { type = INPUT_KEYBOARD, ki_wVk = VK_V },
            new INPUT { type = INPUT_KEYBOARD, ki_wVk = VK_V,       ki_dwFlags = KEYEVENTF_KEYUP },
            new INPUT { type = INPUT_KEYBOARD, ki_wVk = VK_CONTROL, ki_dwFlags = KEYEVENTF_KEYUP },
        };

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, cbSize);
        if (sent != inputs.Length)
        {
            DeckleWhispSource.Log.PasteSendInputPartial();
            DeckleWhispSource.Log.PasteSendInputPartialDetail((int)sent, inputs.Length);
            return false;
        }

        DeckleWhispSource.Log.PasteSucceeded();
        DeckleWhispSource.Log.PasteSent(Win32Util.DescribeHwnd(fg));
        return true;
    }
}
