using Deckle.Autocorrect.Onnx;

namespace Deckle.Autocorrect.Probe;

internal static class SingleProbeCommand
{
    public static int Run(ProbeArguments parsed)
    {
        ModelSpec model = parsed.Models[0];
        if (!Directory.Exists(model.Directory))
        {
            Console.Error.WriteLine($"Missing model directory: {model.Directory}");
            return 1;
        }

        Console.WriteLine($"Model     : {model.Directory}");
        Console.WriteLine($"Margin    : {parsed.Margin:0.###}");
        Console.WriteLine($"Candidates: {parsed.Candidates.Count}");
        Console.WriteLine();

        ISentenceScorer? scorer = OnnxSentenceScorer.TryLoad(model.Directory, parsed.Margin);
        if (scorer is null)
        {
            Console.Error.WriteLine("Model failed to load as an ONNX Runtime GenAI model.");
            return 1;
        }

        try
        {
            SentenceScoringOutcome outcome = scorer.Score(parsed.Candidates);
            if (outcome.Scores.Count > 0)
            {
                foreach (SentenceCandidateScore score in outcome.Scores.OrderByDescending(static s => s.Score))
                {
                    Console.WriteLine(
                        $"{score.Score,10:0.000}  logp={score.LogProbability,10:0.000}  tokens={score.ScoredTokenCount,3}  {score.Text}");
                }

                Console.WriteLine();
            }

            Console.WriteLine($"Chosen    : {outcome.Chosen ?? "(abstain)"}");
            Console.WriteLine($"Margin    : {outcome.Margin:0.###}");
            Console.WriteLine($"Threshold : {outcome.Threshold:0.###}");
            Console.WriteLine($"Abstain   : {outcome.AbstainReason ?? "(none)"}");

            return outcome.AbstainReason is null ? 0 : 3;
        }
        finally
        {
            (scorer as IDisposable)?.Dispose();
        }
    }
}
