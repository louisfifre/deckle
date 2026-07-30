using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Deckle.Autocorrect.Probe;

// Upper-bound oracle for speculative sentence scoring. It reconstructs the
// consented typing stream locally and retains only inter-key timing around the
// first terminal punctuation in a contiguous punctuation run. No typed text is
// emitted. The oracle assumes the punctuation branch is guessed exactly and all
// preparation before model execution is free, so it can falsify an architecture
// but cannot establish that the architecture will attain the reported ceiling.
internal static class AnticipationLeadOracleCommand
{
    public static int Run(ProbeArguments parsed)
    {
        AnticipationLeadFileReport report = AnalyzeFile(parsed.StreamPath, parsed.StreamBytes);
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        }));
        return report.AnalyzedPrefixStable ? 0 : 3;
    }

    internal static AnticipationLeadFileReport AnalyzeFile(string path, long requestedBytes)
    {
        var before = new FileInfo(path);
        long analyzedBytes = requestedBytes > 0 ? requestedBytes : before.Length;
        byte[] prefixBefore = ReadPrefix(path, analyzedBytes);
        string hashBefore = Convert.ToHexString(SHA256.HashData(prefixBefore));
        bool prefixEndedAtLineBoundary = prefixBefore.Length == 0
            || prefixBefore[^1] is (byte)'\n' or (byte)'\r';
        AnticipationLeadAnalysis analysis = AnticipationLeadOracle.Analyze(ReadLines(prefixBefore));
        byte[] prefixAfter = ReadPrefix(path, analyzedBytes);
        string hashAfter = Convert.ToHexString(SHA256.HashData(prefixAfter));
        var after = new FileInfo(path);

        bool analyzedPrefixStable = prefixEndedAtLineBoundary
            && string.Equals(hashBefore, hashAfter, StringComparison.Ordinal);

        return new AnticipationLeadFileReport(
            Path.GetFileName(path),
            after.Length,
            analyzedBytes,
            after.LastWriteTimeUtc,
            hashAfter,
            analyzedPrefixStable,
            prefixEndedAtLineBoundary,
            analysis);
    }

    private static IEnumerable<string> ReadLines(byte[] utf8)
    {
        using var reader = new StringReader(Encoding.UTF8.GetString(utf8));
        while (reader.ReadLine() is { } line)
            yield return line;
    }

    private static byte[] ReadPrefix(string path, long byteCount)
    {
        if (byteCount > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < byteCount)
            throw new EndOfStreamException(
                $"The stream contains {stream.Length} bytes, below the requested prefix {byteCount}.");
        var bytes = new byte[(int)byteCount];
        stream.ReadExactly(bytes);
        return bytes;
    }
}

internal static class AnticipationLeadOracle
{
    private static readonly int[] DecisionBudgetsMilliseconds = [50, 100, 150, 250, 500, 945];
    private static readonly int[] TriggerDelaysMilliseconds = [0, 50, 100, 150, 250];

    public static AnticipationLeadAnalysis Analyze(IEnumerable<string> lines)
    {
        int lineCount = 0;
        int parsedRuns = 0;
        int malformedLines = 0;
        int firstTerminalGestures = 0;
        int unusableTimingGestures = 0;
        int noKnownPrecedingTextGestures = 0;
        var gapMilliseconds = new List<int>();
        var span = new StringBuilder();

        foreach (string line in lines)
        {
            lineCount++;
            if (!TryParseRun(line, out CapturedRun run))
            {
                malformedLines++;
                span.Clear();
                continue;
            }

            parsedRuns++;
            if (run.Erased >= span.Length)
                span.Clear();
            else if (run.Erased > 0)
                span.Length -= run.Erased;

            int[]? gaps = ParseTiming(run.Timing, run.Text.Length);
            for (int index = 0; index < run.Text.Length; index++)
            {
                char current = run.Text[index];
                bool firstTerminal = IsTerminal(current)
                    && (span.Length == 0 || !IsTerminal(span[^1]));
                if (firstTerminal)
                {
                    firstTerminalGestures++;
                    if (span.Length == 0)
                        noKnownPrecedingTextGestures++;
                    else if (gaps is null)
                        unusableTimingGestures++;
                    else
                        gapMilliseconds.Add(gaps[index]);
                }

                span.Append(current);
            }

            if (!ContinuesSpan(run.Closure))
                span.Clear();
        }

        AnticipationReadiness[] readiness = DecisionBudgetsMilliseconds
            .SelectMany(decision => TriggerDelaysMilliseconds.Select(trigger =>
                CreateReadiness(decision, trigger, gapMilliseconds)))
            .ToArray();

        return new AnticipationLeadAnalysis(
            lineCount,
            parsedRuns,
            malformedLines,
            firstTerminalGestures,
            gapMilliseconds.Count,
            unusableTimingGestures,
            noKnownPrecedingTextGestures,
            gapMilliseconds,
            MetricDistribution.Create(gapMilliseconds.Select(static gap => (double)gap)),
            readiness);
    }

    private static AnticipationReadiness CreateReadiness(
        int decisionMilliseconds,
        int triggerDelayMilliseconds,
        IReadOnlyList<int> gapMilliseconds)
    {
        double[] positiveLead = gapMilliseconds
            .Select(gap => (double)gap - triggerDelayMilliseconds - decisionMilliseconds)
            .Where(static lead => lead > 0)
            .ToArray();
        return new AnticipationReadiness(
            decisionMilliseconds,
            triggerDelayMilliseconds,
            positiveLead.Length,
            gapMilliseconds.Count,
            gapMilliseconds.Count == 0 ? 0.0 : (double)positiveLead.Length / gapMilliseconds.Count,
            MetricDistribution.Create(positiveLead));
    }

    private static bool TryParseRun(string line, out CapturedRun run)
    {
        run = default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement payload = document.RootElement.GetProperty("payload");
            run = new CapturedRun(
                payload.GetProperty("text").GetString() ?? string.Empty,
                payload.GetProperty("erased").GetInt32(),
                payload.GetProperty("closure").GetString() ?? string.Empty,
                payload.GetProperty("timing").GetString() ?? string.Empty);
            return run.Erased >= 0;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static int[]? ParseTiming(string timing, int characterCount)
    {
        if (timing.Length == 0)
            return null;
        string[] parts = timing.Split(',');
        if (parts.Length != characterCount)
            return null;

        var gaps = new int[parts.Length];
        for (int index = 0; index < parts.Length; index++)
            if (!int.TryParse(parts[index], out gaps[index]) || gaps[index] < 0)
                return null;
        return gaps;
    }

    private static bool IsTerminal(char value) => value is '.' or '!' or '?' or '…';

    private static bool ContinuesSpan(string closure) => closure is "repair" or "cap";

    private readonly record struct CapturedRun(
        string Text,
        int Erased,
        string Closure,
        string Timing);
}

internal sealed record AnticipationLeadFileReport(
    string SourceFile,
    long SourceFileBytes,
    long AnalyzedBytes,
    DateTime SourceLastWriteUtc,
    string AnalyzedSha256,
    bool AnalyzedPrefixStable,
    bool PrefixEndedAtLineBoundary,
    AnticipationLeadAnalysis Analysis);

internal sealed record AnticipationLeadAnalysis(
    int LineCount,
    int ParsedRuns,
    int MalformedLines,
    int FirstTerminalGestures,
    int UsableTimingGestures,
    int UnusableTimingGestures,
    int NoKnownPrecedingTextGestures,
    IReadOnlyList<int> GapMilliseconds,
    MetricDistribution GapDistributionMilliseconds,
    IReadOnlyList<AnticipationReadiness> Readiness);

internal sealed record AnticipationReadiness(
    int DecisionMilliseconds,
    int TriggerDelayMilliseconds,
    int ReadyBeforeTerminal,
    int EligibleGestures,
    double ReadyRate,
    MetricDistribution PositiveLeadMilliseconds);
