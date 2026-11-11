using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Localization;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // What the take delivers once its raw text is on the clipboard
    // (specs/deckle-dictation-rewriting.md, group Stop):
    //   - a profile's own shortcut rewrites the whole raw text with it;
    //   - else the highest step the raw text reaches decides: streaming, the
    //     take is its passages, already sent during the recording
    //     (TranscriptionEngine.BatchRewrite.cs); at Stop, its profile rewrites
    //     the whole raw text, while what was copied first (the passages read
    //     so far, or the raw text) waits on the clipboard;
    //   - below the first step, the take stays as said.
    //
    // delivered is what the clipboard holds now: the fallback whenever a
    // rewrite fails, so the text the take reports is the text the person has.
    //
    // streaming is the live take's plan and rewriter; null on the monolithic
    // strategy, where the plan is read here and every step rewrites at Stop.
    private FinalizeRewrite ApplyRewrite(
        string rawText, string delivered, bool isFileRun, StreamingRewrite? streaming = null)
    {
        if (isFileRun) return FinalizeRewrite.Verbatim(rawText);

        RewriteStepPlan plan = streaming?.Plan
            ?? RewriteProfileSelection.ForTake(_host.Llm, TakeProfileId, streamingTranscription: false);

        if (plan.Forced is { } forced)
            return RewriteWhole(rawText, delivered, forced);

        if (plan.MissingProfileAbove(rawText.Length) is { } missing)
        {
            DeckleWhispSource.Log.ManualProfileNotFound();
            DeckleWhispSource.Log.ManualProfileNotFoundDetail(missing);
        }

        if (plan.Highest(rawText.Length) is not { Profile: { } profile } top)
            return FinalizeRewrite.Verbatim(delivered);

        DictationBatchRewriter? rewriter = streaming?.Rewriter;
        if (top.Streaming && rewriter is not null)
            return ApplyPassageRewrite(delivered, rewriter, profile);

        if (rewriter?.Profile is { } streamed)
        {
            DeckleLlmSource.Log.BatchRewriteSuperseded();
            DeckleLlmSource.Log.BatchRewriteSupersededDetail(rewriter.BatchCount, streamed.Name, profile.Name);
        }
        return RewriteWhole(rawText, delivered, profile);
    }

    // Rewrites the whole raw text with profile. On success its text replaces
    // what the clipboard holds; on failure the clipboard keeps it, and the
    // rewrite service has already told the person why.
    private FinalizeRewrite RewriteWhole(string rawText, string delivered, RewriteProfile profile)
    {
        RaiseStatus(StatusPhase.Rewriting, Loc.Format("Status_Rewriting_Format", profile.Name));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = _rewrite.Rewrite(rawText, profile);
        stopwatch.Stop();

        string finalText = delivered;
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            // The rewriter's own provider logs why it returned nothing; this
            // records that the take went out unrewritten.
            DeckleWhispSource.Log.RewriteFellBackToRaw("empty_result", stopwatch.ElapsedMilliseconds);
        }
        else if (CopyToClipboard(result.Text))
        {
            finalText = result.Text;
        }
        else
        {
            // The earlier copy stays on the clipboard, and is what the take
            // delivers.
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
            result.EvalTokens,
            result.CacheWriteTokens,
            result.CacheReadTokens);
    }

    // LlmPromptTokens is the input billed at the plain price. A streamed take
    // keeps its profile's prompt in the provider's cache: what was stored and
    // what was read back come apart, each at its own price, and the prompt
    // the model read is the three together.
    private readonly record struct FinalizeRewrite(
        string Text,
        RewriteProfile? Profile,
        long LlmMs,
        long LlmLoadMs,
        long LlmPromptEvalMs,
        long LlmEvalMs,
        int LlmPromptTokens,
        int LlmEvalTokens,
        int LlmCacheWriteTokens = 0,
        int LlmCacheReadTokens = 0)
    {
        public static FinalizeRewrite Verbatim(string text) =>
            new(text, null, 0, 0, 0, 0, 0, 0);
    }
}
