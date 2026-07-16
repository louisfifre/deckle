using System.Linq;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The surface profiler — the corpus ventilation behind the pause pass. These pin
// what the profiles must get right: ventilation by process, the closure mix, the
// slot count read off the timing string (whitespace approximation only for legacy
// records), and gap percentiles cut from inter-slot gaps with the first slot's
// placeholder "0" excluded.
[Trait("Category", "unit")]
public class SurfaceProfilerTests
{
    private static CorpusEntry Entry(
        string process, string closure = "sentence", string timing = "", string typed = "un mot")
        => new(new SentenceCorpus.SentenceRecord(typed, typed, "", closure, timing), true, process);

    [Fact]
    public void VentilatesByProcessBusiestFirst()
    {
        var profiles = SurfaceProfiler.Profile(new[]
        {
            Entry("code.exe"),
            Entry("slack.exe"),
            Entry("slack.exe", closure: "enter"),
        });

        Assert.Equal(new[] { "slack.exe", "code.exe" }, profiles.Select(p => p.Process).ToArray());
        Assert.Equal(2, profiles[0].Sentences);
    }

    [Fact]
    public void CountsTheClosureMix()
    {
        var profile = SurfaceProfiler.Profile(new[]
        {
            Entry("slack.exe", closure: "enter"),
            Entry("slack.exe", closure: "enter"),
            Entry("slack.exe", closure: "sentence"),
            Entry("slack.exe", closure: "interrupted"),
        }).Single();

        Assert.Equal(4, profile.Sentences);
        Assert.Equal(2, profile.EnterClosed);
        Assert.Equal(1, profile.SentenceClosed);
        Assert.Equal(1, profile.Interrupted);
    }

    [Fact]
    public void ReadsSlotCountsAndGapsOffTheTimingString()
    {
        // Three slots, two measured gaps — the leading "0" is a placeholder.
        var profile = SurfaceProfiler.Profile(new[]
        {
            Entry("word.exe", timing: "0,300,900"),
        }).Single();

        Assert.Equal(3, profile.Words);
        Assert.Equal(1, profile.TimedSentences);
        Assert.Equal(2, profile.Gaps.Count);
        Assert.Equal(300, profile.Gaps.P50);
        Assert.Equal(900, profile.Gaps.Max);
    }

    [Fact]
    public void ApproximatesLegacyWordCountsFromTheTypedSide()
    {
        var profile = SurfaceProfiler.Profile(new[]
        {
            Entry("notepad.exe", typed: "trois petits mots."),
        }).Single();

        Assert.Equal(3, profile.Words);
        Assert.Equal(0, profile.TimedSentences);
        Assert.Equal(0, profile.Gaps.Count);
    }

    [Fact]
    public void CutsNearestRankPercentiles()
    {
        // 100 gaps of 1..100 ms: the ranks land exactly on their values.
        var timing = "0," + string.Join(',', Enumerable.Range(1, 100));
        var stats = SurfaceProfiler.Profile(new[] { Entry("x", timing: timing) }).Single().Gaps;

        Assert.Equal(100, stats.Count);
        Assert.Equal(50, stats.P50);
        Assert.Equal(75, stats.P75);
        Assert.Equal(90, stats.P90);
        Assert.Equal(99, stats.P99);
        Assert.Equal(100, stats.Max);
    }

    [Fact]
    public void OverallFoldsEverySurfaceIntoOneBaselineRow()
    {
        var overall = SurfaceProfiler.Overall(new[]
        {
            Entry("a.exe", closure: "enter", timing: "0,100"),
            Entry("b.exe", closure: "sentence", timing: "0,200"),
        });

        Assert.Equal("(all)", overall.Process);
        Assert.Equal(2, overall.Sentences);
        Assert.Equal(2, overall.Gaps.Count);
    }
}
