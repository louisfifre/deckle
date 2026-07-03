using System;
using System.IO;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Onnx;
using Deckle.Core;
using Xunit;

namespace Deckle.Autocorrect.Tests;

public sealed class OnnxSentenceScorerTests
{
    [Fact]
    public void TryLoadReturnsNullWhenDirectoryIsMissing()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Null(OnnxSentenceScorer.TryLoad(missing, margin: 0.0));
    }

    [Fact]
    public void ModelLoadsWhenStaged()
    {
        if (!RunIntegrationTests())
            return;

        string dir = ResolveModelDir();
        if (!IsGenaiModelStaged(dir))
            return;

        ISentenceScorer? scorer = OnnxSentenceScorer.TryLoad(dir, margin: 0.0);
        try
        {
            Assert.NotNull(scorer);
        }
        finally
        {
            (scorer as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ScoresClosedCandidatesWhenModelIsStaged()
    {
        if (!RunIntegrationTests())
            return;

        string dir = ResolveModelDir();
        if (!IsGenaiModelStaged(dir))
            return;

        ISentenceScorer? scorer = OnnxSentenceScorer.TryLoad(dir, margin: 0.0);
        try
        {
            Assert.NotNull(scorer);

            SentenceScoringOutcome outcome = scorer.Score(new[]
            {
                "je suis la",
                "je suis là",
            });

            Assert.Null(outcome.AbstainReason);
            Assert.Equal(2, outcome.Scores.Count);
            Assert.True(double.IsFinite(outcome.Margin));
            foreach (SentenceCandidateScore score in outcome.Scores)
            {
                Assert.True(double.IsFinite(score.Score));
                Assert.True(double.IsFinite(score.LogProbability));
                Assert.True(score.ScoredTokenCount > 0);
            }
        }
        finally
        {
            (scorer as IDisposable)?.Dispose();
        }
    }

    private static string ResolveModelDir()
    {
        string? overrideDir = Environment.GetEnvironmentVariable("DECKLE_ONNX_JUDGE_MODEL_DIR");
        return string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(
                AppPaths.ModelsDirectory,
                "qwen3-0.6b-onnx",
                "onnxruntime",
                "cpu_and_mobile",
                "cpu-int4-kld-block-128")
            : overrideDir;
    }

    private static bool IsGenaiModelStaged(string dir) =>
        Directory.Exists(dir) && File.Exists(Path.Combine(dir, "genai_config.json"));

    private static bool RunIntegrationTests() =>
        string.Equals(
            Environment.GetEnvironmentVariable("DECKLE_ONNX_JUDGE_RUN_INTEGRATION"),
            "1",
            StringComparison.Ordinal);
}
