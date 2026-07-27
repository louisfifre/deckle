using Deckle.Autocorrect.Probe;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class AutocorrectBenchmarkTests
{
    [Fact]
    public void MetricDistributionUsesObservedNearestRanks()
    {
        MetricDistribution distribution = MetricDistribution.Create(
            Enumerable.Range(1, 20).Select(value => (double)value));

        Assert.Equal(10.0, distribution.P50);
        Assert.Equal(19.0, distribution.P95);
        Assert.Equal(20.0, distribution.Maximum);
    }

    [Fact]
    public void ArgumentsSelectAutocorrectBenchmarkAndIterationCount()
    {
        ProbeArguments? parsed = ProbeArguments.Parse(
            ["--autocorrect-benchmark", "--iterations", "37"]);

        Assert.NotNull(parsed);
        Assert.Equal(ProbeMode.AutocorrectBenchmark, parsed.Mode);
        Assert.Equal(37, parsed.Iterations);
        Assert.Empty(parsed.Models);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void ArgumentsRejectInvalidIterationCount(string value)
    {
        Assert.Null(ProbeArguments.Parse(
            ["--autocorrect-benchmark", "--iterations", value]));
    }
}
