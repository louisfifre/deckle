using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deckle.Autocorrect.Probe;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class AnticipationLeadOracleTests
{
    [Fact]
    public void ReconstructsRepairsAndCountsOnlyFirstContiguousTerminalGesture()
    {
        string[] lines =
        [
            Run("ab.", 0, "enter", "0,100,1200"),
            Run("x?!", 0, "enter", "0,200,300"),
            Run("hello", 0, "repair", "0,10,10,10,10"),
            Run(".", 1, "enter", "400"),
            Run(".", 0, "enter", "200"),
        ];

        AnticipationLeadAnalysis report = AnticipationLeadOracle.Analyze(lines);

        Assert.Equal(4, report.FirstTerminalGestures);
        Assert.Equal(3, report.UsableTimingGestures);
        Assert.Equal(1, report.NoKnownPrecedingTextGestures);
        Assert.Equal([1200, 200, 400], report.GapMilliseconds);
    }

    [Fact]
    public void ComputesCurrentQwenReadinessAsAnOracleUpperBound()
    {
        string[] lines =
        [
            Run("ab.", 0, "enter", "0,100,1200"),
            Run("cd?", 0, "enter", "0,100,900"),
            Run("ef!", 0, "enter", "0,100,1000"),
        ];

        AnticipationLeadAnalysis report = AnticipationLeadOracle.Analyze(lines);
        AnticipationReadiness qwen = Assert.Single(report.Readiness, value =>
            value.DecisionMilliseconds == 945 && value.TriggerDelayMilliseconds == 0);

        Assert.Equal(2, qwen.ReadyBeforeTerminal);
        Assert.Equal(3, qwen.EligibleGestures);
        Assert.Equal(2.0 / 3.0, qwen.ReadyRate, 12);
        Assert.Equal(55.0, qwen.PositiveLeadMilliseconds.P50);
        Assert.Equal(255.0, qwen.PositiveLeadMilliseconds.Maximum);
    }

    [Fact]
    public void InvalidTimingDoesNotInventAZeroGap()
    {
        AnticipationLeadAnalysis report = AnticipationLeadOracle.Analyze(
            [Run("ab.", 0, "enter", "0,broken,1200")]);

        Assert.Equal(1, report.FirstTerminalGestures);
        Assert.Equal(0, report.UsableTimingGestures);
        Assert.Equal(1, report.UnusableTimingGestures);
        Assert.Empty(report.GapMilliseconds);
    }

    [Theory]
    [InlineData("1e1000")]
    [InlineData("999999999999999999999999999999")]
    public void MalformedNumericPayloadIsCountedInsteadOfAborting(string erased)
    {
        string line = "{\"payload\":{\"text\":\"secret.\",\"erased\":"
            + erased
            + ",\"closure\":\"enter\",\"timing\":\"0,0,0,0,0,0,1\"}}";

        AnticipationLeadAnalysis report = AnticipationLeadOracle.Analyze([line]);

        Assert.Equal(1, report.MalformedLines);
        Assert.Equal(0, report.ParsedRuns);
    }

    [Fact]
    public void ExactBytePrefixIgnoresGrowingSuffixAndEmitsNoTypedText()
    {
        string secret = "private-secret.";
        string firstLine = Run(secret, 0, "enter", Timing(secret.Length, terminalGap: 1200)) + "\n";
        string suffix = Run("later?", 0, "enter", Timing(6, terminalGap: 300)) + "\n";
        string path = WriteTemporaryStream(firstLine + suffix);

        try
        {
            int prefixBytes = Encoding.UTF8.GetByteCount(firstLine);
            AnticipationLeadFileReport report =
                AnticipationLeadOracleCommand.AnalyzeFile(path, prefixBytes);

            Assert.True(report.AnalyzedPrefixStable);
            Assert.True(report.PrefixEndedAtLineBoundary);
            Assert.Equal(prefixBytes, report.AnalyzedBytes);
            Assert.True(report.SourceFileBytes > report.AnalyzedBytes);
            Assert.Equal(1, report.Analysis.LineCount);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(firstLine))),
                report.AnalyzedSha256);
            string json = JsonSerializer.Serialize(report);
            Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
            Assert.DoesNotContain("later", json, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MidLinePrefixFailsTheStablePrefixGate()
    {
        string line = Run("private.", 0, "enter", Timing(8, terminalGap: 1200)) + "\n";
        string path = WriteTemporaryStream(line);

        try
        {
            AnticipationLeadFileReport report =
                AnticipationLeadOracleCommand.AnalyzeFile(path, Encoding.UTF8.GetByteCount(line) - 2);

            Assert.False(report.PrefixEndedAtLineBoundary);
            Assert.False(report.AnalyzedPrefixStable);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RequestedPrefixLongerThanSourceIsRejected()
    {
        string path = WriteTemporaryStream("{}\n");

        try
        {
            Assert.Throws<EndOfStreamException>(() =>
                AnticipationLeadOracleCommand.AnalyzeFile(path, 100));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Timing(int length, int terminalGap) =>
        string.Join(',', Enumerable.Repeat("0", length - 1).Append(terminalGap.ToString()));

    private static string WriteTemporaryStream(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"deckle-anticipation-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static string Run(string text, int erased, string closure, string timing) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            payload = new { process = "test", text, erased, closure, timing },
        });
}
