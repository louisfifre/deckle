using Deckle.Llm.Rewrite;
using Deckle.Catalog;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    private FinalizeRewrite ApplyRewrite(string rawText, bool isFileRun)
    {
        var llmSettings = _host.Llm;
        RewriteProfile? profile = isFileRun
            ? null
            : RewriteProfileSelection.ForHotkey(llmSettings, _manualProfileName);

        if (!isFileRun
            && llmSettings.Enabled
            && !string.IsNullOrWhiteSpace(_manualProfileName)
            && profile is null)
        {
            DeckleWhispSource.Log.ManualProfileNotFound();
            DeckleWhispSource.Log.ManualProfileNotFoundDetail(_manualProfileName);
        }

        if (profile is null)
            return FinalizeRewrite.Verbatim(rawText);

        RaiseStatus(Loc.Format("Status_Rewriting_Format", profile.Name));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = _rewrite.Rewrite(rawText, llmSettings.OllamaEndpoint, profile);
        stopwatch.Stop();

        string finalText = rawText;
        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            finalText = result.Text;
            // The raw transcript is already safe on the clipboard. A failed
            // replacement therefore degrades to that raw copy without noise.
            CopyToClipboard(finalText);
        }

        return new FinalizeRewrite(
            finalText,
            profile,
            stopwatch.ElapsedMilliseconds,
            result.OllamaLoadMs,
            result.PromptEvalMs,
            result.EvalMs,
            result.PromptTokens,
            result.EvalTokens);
    }

    private readonly record struct FinalizeRewrite(
        string Text,
        RewriteProfile? Profile,
        long LlmMs,
        long OllamaLoadMs,
        long LlmPromptEvalMs,
        long LlmEvalMs,
        int LlmPromptTokens,
        int LlmEvalTokens)
    {
        public static FinalizeRewrite Verbatim(string text) =>
            new(text, null, 0, 0, 0, 0, 0, 0);
    }
}
