using Deckle.Audio;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;
using Deckle.Localization;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── Finalize partial — delivery tail (finalize + clipboard) and the mic-error localizer ──

    // ── Microphone error localization ──────────────────────────────────────────
    //
    // Capture stays free of any Loc.Get dependency; the engine localizes. The
    // raw error code (an HRESULT) goes into the Unavailable body in hex, the
    // form Microsoft documents it in, so users can quote and look it up.
    private static (string Title, string Body) LocalizeMicError(MicErrorKind kind, uint err) => kind switch
    {
        MicErrorKind.NotDetected => (Loc.Get("MicError_NotDetected_Title"), Loc.Get("MicError_NotDetected_Body")),
        MicErrorKind.InUse       => (Loc.Get("MicError_InUse_Title"),       Loc.Get("MicError_InUse_Body")),
        _                        => (Loc.Get("MicError_Unavailable_Title"),
                                     Loc.Format("MicError_Unavailable_Body_Format", MicrophoneCapture.FormatErrorCode(err))),
    };


    // ── Shared finalize ──────────────────────────────────────────────────────
    //
    // Producer-agnostic tail: executes the delivery command, then emits
    // telemetry and optionally raises Finished. Dictation writes the clipboard
    // and may rewrite; imported audio writes an adjacent text file.
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
                FinishTake(
                    fileDelivery.Outcome,
                    fileDelivery.Outcome == TranscriptionOutcome.None ? TakeEndReasons.WriteFailed : null);
            return fileDelivery.Outcome;
        }

        // The raw copy is the safety net for every later stage. A failed rewrite
        // copy therefore leaves useful text behind.
        // Every write lands in the Windows clipboard history, so a take writes the
        // raw text once and at most one rewrite, never per utterance. That is the
        // whole clipboard contract: at most two contents per transcription, the raw
        // text then the rewritten text when a profile is active, never token by
        // token, and the sentence as the grain.
        var clipboardStopwatch = System.Diagnostics.Stopwatch.StartNew();
        bool rawCopySucceeded = CopyToClipboard(rawText);
        clipboardStopwatch.Stop();
        if (!rawCopySucceeded)
        {
            // The clipboard event says why the copy failed; this says the take
            // was lost to the user and whether the recovery file still holds it.
            DeckleWhispSource.Log.DictationNotDelivered("raw_copy_failed", _recovery?.Exists ?? false);
            if (announceCompletion)
                RaiseStatus(StatusPhase.Ready, Loc.Get("Status_Ready"));
            if (announceCompletion)
                FinishTake(TranscriptionOutcome.None, TakeEndReasons.CopyFailed);
            return TranscriptionOutcome.None;
        }

        // The clipboard now holds the text, so the recovery file is no longer
        // its only copy. Cleared here rather than after delivery, so a failure
        // in the rewrite cannot leave a stale file behind.
        ClearRecovery();

        FinalizeRewrite rewrite = ApplyRewrite(rawText, isFileRun, production.BatchRewrite);
        // The surfaces showing the live text learn the final text before the
        // delivery, so what they show when the HUD says Copied is
        // what landed. A batched take already showed each batch; its final
        // text then equals the last snapshot and the reader has nothing to do.
        if (rewrite.Text != rawText)
            RaiseTranscriptChanged(rewrite.Text, TranscriptChangeKind.Revised);
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
            FinishTake(delivered.Outcome);
        return delivered.Outcome;
    }

    // A dictation's delivery is the clipboard copy already made: the text is
    // never pasted, so there is nothing left to do but name the outcome. A file
    // run writes its text file here.
    private FinalizeDelivery DeliverTranscript(string text, TranscriptionDelivery delivery)
    {
        TranscriptionOutcome outcome = delivery.IsFile
            ? WriteFileTranscript(text, delivery.SourceAudioPath!)
            : TranscriptionOutcome.ClipboardOnly;
        return new FinalizeDelivery(outcome);
    }

    private readonly record struct FinalizeDelivery(TranscriptionOutcome Outcome);
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
            DeckleWhispSource.Log.FileTranscriptionWriteFailed(ex.GetType().Name, ex.Message);
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

        // One line per outcome: the copy's figures ride on the line that says
        // how it ended, so a copy no longer takes three lines (alloc, open,
        // complete) to tell what one says.
        if (r.Status == ClipboardWriteStatus.AllocFailed)
        {
            DeckleWhispSource.Log.ClipboardAllocFailed();
            DeckleWhispSource.Log.ClipboardAllocFailedDetail(r.ByteCount, r.Handle);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_ClipboardCopyFailed_Memory_Title"),
                Loc.Get("Engine_ClipboardCopyFailed_Memory_Body"),
                FB_REPLACEMENT);
            return false;
        }

        if (r.Status == ClipboardWriteStatus.OpenFailed)
        {
            DeckleWhispSource.Log.ClipboardOpenFailed(r.ByteCount);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_ClipboardUnavailable_Title"),
                Loc.Get("Engine_ClipboardUnavailable_Body"),
                FB_REPLACEMENT);
            return false;
        }

        if (r.Status == ClipboardWriteStatus.SetDataFailed)
        {
            DeckleWhispSource.Log.ClipboardSetDataFailed(r.ByteCount);
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
        DeckleWhispSource.Log.ClipboardCopyComplete(r.ExpectedChars, r.ByteCount, r.Handle);
        return true;
    }
}
