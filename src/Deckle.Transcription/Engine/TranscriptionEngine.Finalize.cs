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
    // MicErrorKind → (title, body) for UI. Messages formulated for the end user
    // — no Win32 jargon. Raw MMSYSERR code is included verbatim in the
    // Unavailable_Body_Format path so users can paste it back when reporting.
    // Capture itself stays free of any Loc.Get dependency; the engine owns
    // the localization step.
    private static (string Title, string Body) LocalizeMicError(MicErrorKind kind, uint err) => kind switch
    {
        MicErrorKind.NotDetected => (Loc.Get("MicError_NotDetected_Title"), Loc.Get("MicError_NotDetected_Body")),
        MicErrorKind.InUse       => (Loc.Get("MicError_InUse_Title"),       Loc.Get("MicError_InUse_Body")),
        _                        => (Loc.Get("MicError_Unavailable_Title"), Loc.Format("MicError_Unavailable_Body_Format", err)),
    };


    // ── Shared finalize ──────────────────────────────────────────────────────
    //
    // Strategy-agnostic tail of a recording. From the assembled raw text plus
    // the captured audio it writes the clipboard once, resolves and applies an
    // optional LLM rewrite, optionally pastes, then emits the latency + corpus
    // telemetry and raises Finished. Both pipelines — monolithic and streaming —
    // converge here, so the user-facing behaviour is identical whatever produced
    // the text. Synchronous: every step (clipboard, rewrite, paste) is blocking.
    //
    // The producing strategy owns capture, the backend call(s), and the state
    // transitions up to Transcribing; here we only consume the result it hands
    // back. _transcriptionId is generated once per recording by WorkerRun before
    // the strategy runs under the corpus join contract.
    private void FinalizeTranscription(PipelineProduction production)
    {
        string rawText = production.RawText;
        bool isFileRun = _fileTranscriptionPath is not null;
        double recordingDurationSec = _recordingSw?.Elapsed.TotalSeconds ?? 0;

        // The raw copy is the safety net for every later stage. A failed rewrite
        // copy therefore leaves useful text behind, and paste never sees stale data.
        var clipboardStopwatch = System.Diagnostics.Stopwatch.StartNew();
        bool rawCopySucceeded = CopyToClipboard(rawText);
        clipboardStopwatch.Stop();
        if (!rawCopySucceeded)
        {
            // The file remains the primary deliverable for imported audio, but
            // the clipboard error must remain the user-facing outcome.
            if (isFileRun)
                WriteFileTranscript(rawText);
            RaiseStatus(Loc.Get("Status_Ready"));
            RaiseFinished(TranscriptionOutcome.None);
            return;
        }

        FinalizeRewrite rewrite = ApplyRewrite(rawText, isFileRun);
        FinalizeDelivery delivery = DeliverTranscript(rewrite.Text, isFileRun);

        RecordPipelineMetrics(
            production,
            rawText,
            rewrite,
            delivery,
            clipboardStopwatch.ElapsedMilliseconds,
            isFileRun,
            recordingDurationSec);
        RaiseFinished(delivery.Outcome);
    }

    private FinalizeDelivery DeliverTranscript(string text, bool isFileRun)
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

        TranscriptionOutcome outcome = isFileRun
            ? WriteFileTranscript(text)
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
    // Writes the transcript to a .txt named after the source audio file, beside
    // that source. Called only
    // on a file run — normally after a successful clipboard copy (a write failure
    // then degrades to ClipboardOnly rather than losing the result), and best-effort
    // when the copy itself failed, so the disk keeps the text either way. The catch
    // covers the filesystem exceptions plus the invalid-path family. Anything
    // else is a genuine bug and propagates to the worker's crash handler.
    private TranscriptionOutcome WriteFileTranscript(string fullText)
    {
        string audioPath = _fileTranscriptionPath ?? "";

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
            return TranscriptionOutcome.ClipboardOnly;
        }
    }

    // ── Presse-papier ─────────────────────────────────────────────────────────

    // Returns true on a successful copy + verified read-back. False on any of
    // the three fatal branches (GlobalAlloc, OpenClipboard, SetClipboardData) —
    // each surfaces a Critical UserFeedback. Verify-length mismatch only emits
    // a Warning since the bytes reached the clipboard; the length check is a
    // safety net against clipboard-format mangling by a third-party watcher.
    private bool CopyToClipboard(string text)
    {
        // The Win32 write + read-back verification now lives in
        // Deckle.Core.Win32Clipboard, shared with the LogWindow Copy
        // command. This method keeps the engine's observability surface: it
        // maps the structured result back onto the same EventSource events and
        // UserFeedback the inline implementation emitted, in the same order.
        ClipboardWriteResult r = Win32Clipboard.TryCopyText(text);

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

        // The clipboard opened successfully for every remaining branch.
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

        // Bytes reached the OS clipboard. The two Verify states are advisory
        // Warnings — the read-back flagged a discrepancy but the copy landed.
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

    // Sends Ctrl+V to whatever window currently has the foreground at Stop
    // time — but only when UI Automation confirms the focused element is a
    // text-accepting control (Edit or Document). No Start-time capture, no
    // bring-to-front, no focus comparison: the user had all the time of the
    // recording + transcription to place their cursor where they want.
    //
    // Doctrine: clipboard is the safe default. Paste only when we are confident
    // the target expects text. When in doubt — UIA refuses to answer, unknown
    // control type, foreground is Deckle itself — the text stays on the
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

        // UI Automation probe on the currently focused element. If the probe
        // is anything other than "yes, it's an Edit or Document", we bail out
        // to the clipboard-only path. No speculative paste.
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
