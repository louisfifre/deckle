using System.Text.Json;
using Deckle.Core;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Replays Louis's consented raw typing stream through the real keyboard-facing
// engine harness. This is a robustness probe, not a truth benchmark: historical
// text contains both intended literals and mistakes. The hard assertion is that
// every attempted correction matches the actual simulated screen suffix; named
// scenarios carry the reviewed linguistic expectations separately.
[Trait("Category", "maintenance")]
public sealed class AutocorrectTypingStreamReplayMaintenanceTests
{
    private readonly ITestOutputHelper _out;

    public AutocorrectTypingStreamReplayMaintenanceTests(ITestOutputHelper output) => _out = output;

    [Fact(Explicit = true)]
    public void ReplaysCollectedPhysicalTypingWithoutUnknownSuffixWrites()
    {
        string? path = FindStream();
        Assert.SkipUnless(path is not null, "no autocorrect typing stream collected yet");

        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        FrequencyLexicon french = FrequencyLexicon.LoadTsvGz(Path.Combine(
            dataDir, AutocorrectLexiconArtifacts.FrenchFileName));
        var english = new GlobalEnglishLexicon(
            AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(dataDir));
        var index = AccentIndex.Build(french);
        string pairPath = Path.Combine(
            dataDir, AutocorrectLexiconArtifacts.PairBigramsFrenchFileName);
        IPairDisambiguator? context = File.Exists(pairPath)
            ? BigramPairDisambiguator.LoadTsvGz(pairPath)
            : null;
        var policy = new CompositeCorrectionPolicy(
            new DiacriticsRestorer(french, english, index, context: context),
            new ElisionCorrector(french, english),
            new ConservativeTypoCorrector(french, english, accentIndex: index));

        AutocorrectEngineHarness? harness = null;
        string activeProcess = string.Empty;
        int runs = 0, spans = 0, characters = 0, backspaces = 0;
        int corrections = 0, suffixMismatches = 0, malformed = 0;

        void CloseSpan()
        {
            if (harness is null) return;
            corrections += harness.Applied.Count;
            suffixMismatches += harness.InjectionFailures.Count;
            harness.Dispose();
            harness = null;
            activeProcess = string.Empty;
        }

        foreach (string line in File.ReadLines(path!))
        {
            if (!TryRead(line, out CapturedRun run))
            {
                malformed++;
                CloseSpan();
                continue;
            }

            runs++;
            if (harness is null || !string.Equals(activeProcess, run.Process, StringComparison.OrdinalIgnoreCase))
            {
                CloseSpan();
                activeProcess = run.Process.Length > 0 ? run.Process : "notepad";
                harness = new AutocorrectEngineHarness(policy, french: french, english: english);
                harness.Settings.Apps[activeProcess] = true;
                harness.Prober.Surface = AutocorrectEngineHarness.Editable(activeProcess);
                Assert.True(harness.Start());
                spans++;
            }

            for (int i = 0; i < run.Erased; i++)
                harness.Backspace();
            backspaces += run.Erased;

            int[] gaps = ParseTiming(run.Timing, run.Text.Length);
            for (int i = 0; i < run.Text.Length; i++)
            {
                harness.TimeMs += gaps[i];
                harness.Type(run.Text[i].ToString());
            }
            characters += run.Text.Length;

            if (!ContinuesSpan(run.Closure))
                CloseSpan();
        }
        CloseSpan();

        _out.WriteLine(
            $"{runs} runs / {spans} spans / {characters} chars / {backspaces} backspaces / "
            + $"{corrections} corrections / {suffixMismatches} suffix mismatches / {malformed} malformed lines");
        Assert.True(runs > 0, "the stream contained no replayable run");
        Assert.Equal(0, suffixMismatches);
    }

    private static bool TryRead(string line, out CapturedRun run)
    {
        run = default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement payload = document.RootElement.GetProperty("payload");
            run = new CapturedRun(
                payload.GetProperty("process").GetString() ?? string.Empty,
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
    }

    private static int[] ParseTiming(string timing, int characterCount)
    {
        var gaps = new int[characterCount];
        if (timing.Length == 0)
            return gaps;
        string[] parts = timing.Split(',');
        if (parts.Length != characterCount)
            return gaps;
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out gaps[i]) || gaps[i] < 0)
                gaps[i] = 0;
        return gaps;
    }

    private static bool ContinuesSpan(string closure) =>
        closure is "repair" or "cap";

    private static string? FindStream() => new[]
    {
        Path.Combine(AppPaths.TelemetryDirectory, "validation", "autocorrect.stream.jsonl"),
        Path.Combine(AppPaths.TelemetryDirectory, "autocorrect.stream.jsonl"),
    }.FirstOrDefault(File.Exists);

    private readonly record struct CapturedRun(
        string Process,
        string Text,
        int Erased,
        string Closure,
        string Timing);
}
