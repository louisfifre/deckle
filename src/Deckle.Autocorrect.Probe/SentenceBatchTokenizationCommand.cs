using System.Text.Json;
using Deckle.Autocorrect.Onnx;

namespace Deckle.Autocorrect.Probe;

internal static class SentenceBatchTokenizationCommand
{
    public static int Run(ProbeArguments parsed)
    {
        ModelSpec model = parsed.Models[0];
        if (!Directory.Exists(model.Directory))
        {
            Console.Error.WriteLine($"Missing model directory: {model.Directory}");
            return 1;
        }

        try
        {
            using var scorer = new OnnxSentenceScorer(
                model.Directory,
                margin: 0.0,
                parsed.Provider);
            SentenceBatchFixtureSelection fixture =
                SentenceBatchExperimentCommand.SelectFixture(
                    CorrectionBenchmarkCorpus.All,
                    scorer.InspectBatchInputExperimental);
            SentenceBatchTokenizationInspection forward =
                scorer.InspectBatchTokenizationExperimental(fixture.Case.Candidates);
            SentenceBatchTokenizationInspection reverse =
                scorer.InspectBatchTokenizationExperimental(
                    fixture.Case.Candidates.Reverse().ToArray());
            bool hypothesisPassed = HypothesisPasses(forward, reverse);
            var report = new SentenceBatchTokenizationReport(
                model.Label,
                model.Directory,
                parsed.Provider,
                fixture.CorpusIndex,
                fixture.Case.Id,
                fixture.Case.Category,
                fixture.Geometry,
                forward,
                reverse,
                hypothesisPassed);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            }));
            return forward.TechnicallyValid && reverse.TechnicallyValid ? 0 : 3;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name);
            return 1;
        }
    }

    internal static bool HypothesisPasses(
        SentenceBatchTokenizationInspection forward,
        SentenceBatchTokenizationInspection reverse) =>
        Passes(forward) && Passes(reverse);

    private static bool Passes(SentenceBatchTokenizationInspection inspection) =>
        inspection.TechnicallyValid
        && inspection.BosTokenId is not null
        && inspection.BatchEntriesIdentical
        && inspection.BatchMatchesRaw
        && !inspection.BatchMatchesPrepared
        && inspection.NormalizedRawMatchesPrepared
        && inspection.PrependedBosBatchMatchesPrepared
        && inspection.FirstBatchRawMismatch is null
        && inspection.FirstBatchPreparedMismatch == 0
        && inspection.FirstPrependedPreparedMismatch is null;
}

internal sealed record SentenceBatchTokenizationReport(
    string ModelLabel,
    string ModelDirectory,
    string Provider,
    int FixtureCorpusIndex,
    string FixtureId,
    string FixtureCategory,
    SentenceBatchInputGeometry FixtureGeometry,
    SentenceBatchTokenizationInspection Forward,
    SentenceBatchTokenizationInspection Reverse,
    bool HypothesisPassed);
