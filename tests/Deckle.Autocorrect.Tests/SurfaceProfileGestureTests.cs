using System.IO;
using System.Linq;
using Deckle.Autocorrect.Lab;
using Deckle.Core;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The maintainer's ventilation gesture: fold the collected typed-sentence corpus
// into per-surface profiles and write the markdown report next to it. Read-only
// over the corpus, no model, seconds — a silent skip when no corpus is collected
// yet (CI, a fresh clone).
[Trait("Category", "gesture")]
public sealed class SurfaceProfileGestureTests
{
    private readonly ITestOutputHelper _out;

    public SurfaceProfileGestureTests(ITestOutputHelper output) => _out = output;

    // The corpus lands either directly under telemetry/ or in its validation
    // subfolder, depending on the sink's configuration — take whichever exists.
    private static string? FindCorpus() => new[]
    {
        Path.Combine(AppPaths.TelemetryDirectory, "validation", "autocorrect.text.jsonl"),
        Path.Combine(AppPaths.TelemetryDirectory, "autocorrect.text.jsonl"),
    }.FirstOrDefault(File.Exists);

    [Fact]
    public void VentilatesTheCollectedCorpusIntoSurfaceProfiles()
    {
        string? corpusPath = FindCorpus();
        Assert.SkipUnless(corpusPath is not null, "no typed-text corpus collected yet");

        var entries = CorpusReader.Read(corpusPath!).ToList();
        Assert.SkipUnless(entries.Count > 0, "the corpus holds no parseable record yet");

        var profiles = SurfaceProfiler.Profile(entries);
        var overall = SurfaceProfiler.Overall(entries);

        string reportPath = Path.Combine(
            Path.GetDirectoryName(corpusPath!)!, "autocorrect.surface-profiles.md");
        File.WriteAllText(reportPath, SurfaceProfileReport.Render(overall, profiles));

        _out.WriteLine($"{entries.Count} records over {profiles.Count} surfaces.");
        _out.WriteLine($"Surface-profile report → {reportPath}");

        Assert.NotEmpty(profiles);
    }
}
