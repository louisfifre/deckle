using Deckle.Autocorrect.Probe;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Versioned gold corpus for the deterministic, instant correction path. The
// shared benchmark runner types every phrase as physical key events through the
// production policy set. Wrong changes are a hard veto; recall and exact-sentence
// rate expose residue.
[Trait("Category", "integration")]
public sealed class AutocorrectKeyboardQualityTests(ITestOutputHelper output)
{
    [Fact]
    public void ProductionKeyboardCorpusMeetsPrecisionFirstQualityGate()
    {
        // The lexicons ride beside the binary under Data\, never flat.
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        KeyboardQualitySummary summary =
            AutocorrectBenchmark.MeasureKeyboardQuality(dataDir);
        string score =
            $"quality: precision={summary.Precision:P1} "
            + $"({summary.TrueChanges}/{summary.TrueChanges + summary.WrongChanges}), "
            + $"recall={summary.Recall:P1} "
            + $"({summary.TrueChanges}/{summary.GoldChanges}), "
            + $"exact={summary.ExactRate:P1} "
            + $"({summary.ExactScenarios}/{summary.ScenarioCount}), "
            + $"wrong={summary.WrongChanges}";

        output.WriteLine(score);
        foreach (string failure in summary.Failures)
            output.WriteLine(failure);

        string detail = score + Environment.NewLine
            + string.Join(Environment.NewLine, summary.Failures);
        Assert.True(summary.WrongChanges == 0, detail);
        Assert.True(summary.Recall >= 0.90, detail);
        Assert.True(summary.ExactRate >= 0.85, detail);
    }
}
