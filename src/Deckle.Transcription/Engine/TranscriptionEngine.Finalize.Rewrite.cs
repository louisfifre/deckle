using Deckle.Llm.Rewrite;
using Deckle.Catalog;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // batchRewrite is the live take's batch run, when it had one: its batches
    // were already sent during the recording, so the take is assembled from
    // them (TranscriptionEngine.BatchRewrite.cs) instead of rewritten whole.
    private FinalizeRewrite ApplyRewrite(
        string rawText, bool isFileRun, BatchRewriteRun? batchRewrite = null)
    {
        if (batchRewrite is not null && !isFileRun)
            return ApplyBatchRewrite(rawText, batchRewrite);

        var llmSettings = _host.Llm;
        RewriteProfile? profile = isFileRun
            ? null
            : RewriteProfileSelection.ForDictation(llmSettings);

        if (!isFileRun
            && llmSettings.Enabled
            && RewriteProfileSelection.HasActiveProfile(llmSettings)
            && profile is null)
        {
            DeckleWhispSource.Log.ManualProfileNotFound();
            DeckleWhispSource.Log.ManualProfileNotFoundDetail(
                llmSettings.ActiveProfileName ?? llmSettings.ActiveProfileId ?? "");
        }

        if (profile is null)
            return FinalizeRewrite.Verbatim(rawText);

        RaiseStatus(Loc.Format("Status_Rewriting_Format", profile.Name));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = _rewrite.Rewrite(rawText, profile);
        stopwatch.Stop();

        string finalText = rawText;
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            // The rewriter's own provider logs why it returned nothing; this
            // records that the take went out unrewritten.
            DeckleWhispSource.Log.RewriteFellBackToRaw("empty_result", stopwatch.ElapsedMilliseconds);
        }
        else
        {
            finalText = result.Text;
            // The raw transcript is already on the clipboard, so a failed
            // replacement leaves that raw copy in place.
            if (!CopyToClipboard(finalText))
                DeckleWhispSource.Log.RewriteFellBackToRaw("copy_failed", stopwatch.ElapsedMilliseconds);
        }

        return new FinalizeRewrite(
            finalText,
            profile,
            stopwatch.ElapsedMilliseconds,
            result.LoadMs,
            result.PromptEvalMs,
            result.EvalMs,
            result.PromptTokens,
            result.EvalTokens);
    }

    private readonly record struct FinalizeRewrite(
        string Text,
        RewriteProfile? Profile,
        long LlmMs,
        long LlmLoadMs,
        long LlmPromptEvalMs,
        long LlmEvalMs,
        int LlmPromptTokens,
        int LlmEvalTokens)
    {
        public static FinalizeRewrite Verbatim(string text) =>
            new(text, null, 0, 0, 0, 0, 0, 0);
    }
}
