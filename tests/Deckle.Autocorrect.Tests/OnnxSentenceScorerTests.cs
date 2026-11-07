using System;
using System.IO;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Onnx;
using Deckle.Core;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Collection(OnnxJudgeSerialCollection.Name)]
public sealed class OnnxSentenceScorerTests
{
    [Fact]
    [Trait("Category", "integration")]
    public void TryLoadReturnsNullWhenDirectoryIsMissing()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Null(OnnxSentenceScorer.TryLoad(
            missing, margin: 0.0, executionProvider: "dml", out Exception? error));
        Assert.Null(error);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void TryLoadReportsWhyAPresentModelCannotLoad()
    {
        string invalid = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.Null(OnnxSentenceScorer.TryLoad(
                invalid, margin: 0.0, executionProvider: "dml", out Exception? error));
            Assert.NotNull(error);
        }
        finally
        {
            Directory.Delete(invalid);
        }
    }

    // Each load once owned an OgaHandle and released it on failure as on
    // dispose; releasing one shuts ONNX Runtime GenAI down for the whole
    // process, and the next model construction died on an access violation
    // no catch can stop. A config whose model file is missing reaches model
    // construction without a real model or a GPU, so two failed loads replay
    // what a domain-pack rebuild did to the live judge.
    [Fact]
    [Trait("Category", "regression")]
    public void ALoadAfterAnEarlierOneWasReleasedReportsItsOwnOutcome()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "genai_config.json"), ConfigWithoutModelFile);

            for (int attempt = 0; attempt < 2; attempt++)
            {
                Assert.Null(OnnxSentenceScorer.TryLoad(
                    dir, margin: 0.0, executionProvider: "cpu", out Exception? error));
                Assert.NotNull(error);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // A decoder-only config that parses, then names a model file the
    // directory does not hold.
    private const string ConfigWithoutModelFile = """
        {
          "model": {
            "type": "qwen3",
            "context_length": 64,
            "vocab_size": 16,
            "bos_token_id": 0,
            "eos_token_id": 1,
            "pad_token_id": 0,
            "decoder": {
              "filename": "model.onnx",
              "head_size": 8,
              "hidden_size": 16,
              "num_attention_heads": 2,
              "num_hidden_layers": 1,
              "num_key_value_heads": 1,
              "inputs": {
                "input_ids": "input_ids",
                "attention_mask": "attention_mask",
                "position_ids": "position_ids",
                "past_key_names": "past_key_values.%d.key",
                "past_value_names": "past_key_values.%d.value"
              },
              "outputs": {
                "logits": "logits",
                "present_key_names": "present.%d.key",
                "present_value_names": "present.%d.value"
              }
            }
          },
          "search": { "max_length": 64 }
        }
        """;

    [Fact]
    [Trait("Category", "system")]
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
    [Trait("Category", "system")]
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

    [Fact]
    [Trait("Category", "system")]
    public void ProfiledScoringPreservesTheOrdinaryOutcomeWhenModelIsStaged()
    {
        if (!RunIntegrationTests())
            return;

        string dir = ResolveModelDir();
        if (!IsGenaiModelStaged(dir))
            return;

        using var scorer = new OnnxSentenceScorer(dir, margin: 0.0);
        string[] candidates = ["je suis la", "je suis là"];

        SentenceScoringOutcome ordinary = scorer.Score(candidates);
        ProfiledSentenceScoringOutcome profiled = scorer.ScoreProfiled(candidates);

        Assert.Equal(ordinary.Chosen, profiled.Outcome.Chosen);
        Assert.Equal(ordinary.AbstainReason, profiled.Outcome.AbstainReason);
        Assert.Equal(
            ordinary.Scores.Select(static score => score.Score),
            profiled.Outcome.Scores.Select(static score => score.Score));
        Assert.Equal(2, profiled.Profile.Orders.Count);
        Assert.All(profiled.Profile.Orders, order =>
            Assert.Equal(candidates.Length, order.Candidates.Count));
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
