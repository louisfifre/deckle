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

        Assert.Equal(20, distribution.Count);
        Assert.Equal(10.0, distribution.P50);
        Assert.Equal(19.0, distribution.P95);
        Assert.Equal(20.0, distribution.P99);
        Assert.Equal(20.0, distribution.Maximum);
    }

    [Fact]
    public void EmptyMetricDistributionIsNotReportedAsZeroLatency()
    {
        MetricDistribution distribution = MetricDistribution.Create(Array.Empty<double>());

        Assert.Equal(0, distribution.Count);
        Assert.Null(distribution.P50);
        Assert.Null(distribution.P95);
        Assert.Null(distribution.P99);
        Assert.Null(distribution.Maximum);
    }

    [Fact]
    public void KeyboardPrecisionIsNotMeasuredWithoutAnEmittedEdit()
    {
        var summary = new KeyboardQualitySummary(
            ScenarioCount: 1,
            GoldChanges: 1,
            TrueChanges: 0,
            WrongChanges: 0,
            ExactScenarios: 0,
            Failures: Array.Empty<string>());

        Assert.Null(summary.InternalEditPairPrecision);
        Assert.Null(summary.AppliedCorrectionPrecision);
        string json = System.Text.Json.JsonSerializer.Serialize(summary);
        Assert.Contains("\"InternalEditPairPrecision\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"AppliedCorrectionPrecision\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ArgumentsSelectAutocorrectBenchmarkAndIterationCount()
    {
        ProbeArguments? parsed = ProbeArguments.Parse(
            ["--autocorrect-benchmark", "--iterations", "37"]);

        Assert.NotNull(parsed);
        Assert.Equal(ProbeMode.AutocorrectBenchmark, parsed.Mode);
        Assert.Equal(37, parsed.Iterations);
        Assert.False(parsed.Json);
        Assert.Empty(parsed.Models);
    }

    [Fact]
    public void ArgumentsSelectMachineReadableAutocorrectBenchmark()
    {
        ProbeArguments? parsed = ProbeArguments.Parse(
            ["--autocorrect-benchmark", "--iterations", "37", "--json"]);

        Assert.NotNull(parsed);
        Assert.Equal(ProbeMode.AutocorrectBenchmark, parsed.Mode);
        Assert.Equal(37, parsed.Iterations);
        Assert.True(parsed.Json);
    }

    [Theory]
    [InlineData("--benchmark")]
    [InlineData("--caret-context")]
    public void JsonIsRejectedOutsideAutocorrectBenchmark(string mode)
    {
        Assert.Null(ProbeArguments.Parse([mode, "--json"]));
    }

    [Fact]
    public void ArgumentsSelectStaleWorkProbeAndIterationCount()
    {
        ProbeArguments? parsed = ProbeArguments.Parse(
            ["--stale-work-probe", "--iterations", "7"]);

        Assert.NotNull(parsed);
        Assert.Equal(ProbeMode.StaleWork, parsed.Mode);
        Assert.Equal(7, parsed.Iterations);
        Assert.Empty(parsed.Models);
    }

    [Fact]
    public void ArgumentsSelectAnticipationLeadOracleAndStream()
    {
        ProbeArguments? parsed = ProbeArguments.Parse(
            ["--anticipation-lead-oracle", "--stream", "typing.jsonl", "--stream-bytes", "123"]);

        Assert.NotNull(parsed);
        Assert.Equal(ProbeMode.AnticipationLead, parsed.Mode);
        Assert.Equal("typing.jsonl", parsed.StreamPath);
        Assert.Equal(123, parsed.StreamBytes);
        Assert.Empty(parsed.Models);
    }

    [Fact]
    public void AnticipationLeadOracleRequiresAStream()
    {
        Assert.Null(ProbeArguments.Parse(["--anticipation-lead-oracle"]));
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
