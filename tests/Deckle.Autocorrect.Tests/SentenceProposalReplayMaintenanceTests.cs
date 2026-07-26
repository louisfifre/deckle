using System.Text;
using Deckle.Autocorrect.Lab;
using Deckle.Autocorrect.Onnx;
using Deckle.Core;
using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Supervised shadow study for the proposed sentence-stage replacement. It feeds
// the text left by the current pipeline (corpus `final`, not raw `typed`) to the
// LLM proposer, diagnoses the generated diff, then asks the closed ONNX judge to
// choose between explicit KEEP and that proposal. It writes review material only;
// no live surface and no injector exist in this path.
[Trait("Category", "maintenance")]
[Collection(OnnxJudgeSerialCollection.Name)]
public sealed class SentenceProposalReplayMaintenanceTests
{
    private readonly ITestOutputHelper _out;

    public SentenceProposalReplayMaintenanceTests(ITestOutputHelper output) => _out = output;

    [Fact(Explicit = true)]
    public void ReviewsWholeSentenceProposalsOverTheCollectedCorpus()
    {
        string? corpusPath = FindCorpus();
        Assert.SkipUnless(corpusPath is not null, "no typed-text corpus collected yet");

        string judgeDir = Path.Combine(AppPaths.ModelsDirectory, "sentence-judge");
        Assert.SkipUnless(Directory.Exists(judgeDir), "sentence judge not staged");

        string endpoint = LlmSettingsService.Instance.Current.OllamaEndpoint;
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(endpoint), "Ollama endpoint not configured");

        int limit = ReplayLimit();
        string executionProvider = Environment.GetEnvironmentVariable("DECKLE_ONNX_JUDGE_EP")
            is { Length: > 0 } configuredProvider
                ? configuredProvider
                : "dml";

        Console.Error.WriteLine(
            $"[proposal-replay] loading judge — ep={executionProvider}, limit={limit}, dir={judgeDir}");

        // Zero margin exposes raw top-1 plus its gap. The report is review data for
        // calibrating a separate whole-sentence margin; the slot margin of 1.0 is
        // deliberately not imported into this different task.
        using var scorer = new OnnxSentenceScorer(judgeDir, margin: 0.0, executionProvider);
        var verifier = new SentenceProposalVerifier(scorer);
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var proposalGate = new SentenceProposalGate(new GlobalEnglishLexicon(
            AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(dataDir)));
        var proposer = new RewriteService();

        var report = new StringBuilder();
        report.AppendLine("# Sentence proposal verification shadow")
              .AppendLine()
              .AppendLine($"Corpus: `{corpusPath}`  ")
              .AppendLine($"Model: `{SentenceRewrite.Model}`  ")
              .AppendLine($"Judge EP: `{executionProvider}`  ")
              .AppendLine($"Limit: {limit}")
              .AppendLine()
              .AppendLine("> `Judge accept` measures language preference. `Joint safe accept` additionally requires the strict silent-correction gate; only the latter is eligible for a future live write.")
              .AppendLine();

        int examined = 0;
        int proposals = 0;
        int judgeAccepted = 0;
        int jointSafeAccepted = 0;
        int kept = 0;
        int abstained = 0;
        int generationFailures = 0;
        int consecutiveFailures = 0;

        foreach (CorpusEntry entry in CorpusReader.Read(corpusPath!))
        {
            if (examined >= limit)
                break;
            if (!string.Equals(entry.Record.Closure, "sentence", StringComparison.Ordinal))
                continue;

            string original = entry.Record.Final.Trim();
            if (original.Length == 0)
                continue;

            examined++;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            deadline.CancelAfter(SentenceRewrite.Deadline);

            RewriteResult generated = proposer.RewriteSentence(original, endpoint, deadline.Token);
            string? proposed = generated.Text?.Trim();
            if (string.IsNullOrWhiteSpace(proposed))
            {
                generationFailures++;
                consecutiveFailures++;
                Console.Error.WriteLine($"  [{examined}] no proposal");
                if (consecutiveFailures >= 3)
                    break; // endpoint/model unavailable: do not repeat the same slow failure.
                continue;
            }

            consecutiveFailures = 0;
            proposals++;
            DiffGateVerdict diff = RewriteDiffGate.Evaluate(original, proposed);
            SentenceProposalGateVerdict safety = proposalGate.Evaluate(
                original, proposed, entry.Record.Typed.Trim());
            SentenceProposalVerification verification = verifier.Verify(original, proposed);

            switch (verification.Verdict)
            {
                case SentenceProposalVerdict.Accept:
                    judgeAccepted++;
                    if (safety.Accepted) jointSafeAccepted++;
                    break;
                case SentenceProposalVerdict.Keep: kept++; break;
                case SentenceProposalVerdict.Abstain: abstained++; break;
            }

            report.AppendLine($"## {examined}. {entry.Day} · {entry.Process}")
                  .AppendLine()
                  .AppendLine($"- Verdict: `{verification.Verdict}`")
                  .AppendLine($"- Joint safe accept: `{(verification.Verdict == SentenceProposalVerdict.Accept && safety.Accepted ? "yes" : "no")}`")
                  .AppendLine($"- Safety gate: `{safety.Reason}`")
                  .AppendLine($"- Margin: `{verification.Margin:0.000}` (raw top-1; threshold to calibrate)")
                  .AppendLine($"- Diff diagnostic: `{(diff.Accepted ? "accepted" : "rejected")}`")
                  .AppendLine($"- Reason: `{verification.Reason ?? "none"}`")
                  .AppendLine()
                  .AppendLine("Typed before commit-stage edits:")
                  .AppendLine()
                  .AppendLine($"> {AsQuote(entry.Record.Typed.Trim())}")
                  .AppendLine()
                  .AppendLine("Original:")
                  .AppendLine()
                  .AppendLine($"> {AsQuote(original)}")
                  .AppendLine()
                  .AppendLine("Proposal:")
                  .AppendLine()
                  .AppendLine($"> {AsQuote(proposed)}")
                  .AppendLine();

            foreach (SentenceCandidateScore score in verification.Scores)
                report.AppendLine(
                    $"- score `{score.Score:0.000}` · logp `{score.LogProbability:0.000}` · tokens `{score.ScoredTokenCount}` · {score.Text}");
            report.AppendLine();

            Console.Error.WriteLine(
                $"  [{examined}] {verification.Verdict} margin={verification.Margin:0.000} safety={safety.Reason} diff={(diff.Accepted ? "pass" : "block")}");
        }

        report.AppendLine("## Summary")
              .AppendLine()
              .AppendLine(
                  $"Examined: {examined} · Proposals: {proposals} · Judge accept: {judgeAccepted} · Joint safe accept: {jointSafeAccepted} · Keep: {kept} · Abstain: {abstained} · Generation failures: {generationFailures}");

        string reportPath = Path.Combine(
            Path.GetDirectoryName(corpusPath!)!,
            "autocorrect.proposal-verification.md");
        File.WriteAllText(reportPath, report.ToString());

        _out.WriteLine(
            $"{proposals} proposals reviewed ({judgeAccepted} judge accept / {jointSafeAccepted} joint safe accept / {kept} keep / {abstained} abstain).");
        _out.WriteLine($"Shadow report → {reportPath}");
        Assert.True(proposals > 0, "the configured proposer returned no sentence proposal");
    }

    private static string? FindCorpus() => new[]
    {
        Path.Combine(AppPaths.TelemetryDirectory, "validation", "autocorrect.text.jsonl"),
        Path.Combine(AppPaths.TelemetryDirectory, "autocorrect.text.jsonl"),
    }.FirstOrDefault(File.Exists);

    private static int ReplayLimit()
    {
        string? raw = Environment.GetEnvironmentVariable("DECKLE_PROPOSAL_REPLAY_LIMIT");
        return int.TryParse(raw, out int value) && value > 0 ? value : 50;
    }

    private static string AsQuote(string text) => text.Replace("\n", "  \n> ", StringComparison.Ordinal);
}
