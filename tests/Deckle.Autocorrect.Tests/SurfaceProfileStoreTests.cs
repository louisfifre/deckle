using System.IO;
using System.Linq;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The live surface-profile contract, both ends: the store the engine reads
// (tolerant — a missing or corrupt artifact disarms the pause pass, never a
// boot failure) and the Lab render that writes it (qualified surfaces only,
// the provisional threshold formula quarantined in one place).
public class SurfaceProfileStoreTests
{
    [Fact]
    [Trait("Category", "integration")]
    public void LoadsProfilesAndClampsNegativeThresholds()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(path, """
            [
              { "process": "ChatGPT", "pauseThresholdMs": 2400 },
              { "process": "broken", "pauseThresholdMs": -5 },
              { "pauseThresholdMs": 100 }
            ]
            """);
        try
        {
            var records = SurfaceProfileStore.Load(path);

            Assert.Equal(2, records.Count); // the process-less record is skipped
            Assert.Equal(2400, records[0].PauseThresholdMs);
            Assert.Equal(0, records[1].PauseThresholdMs); // clamped, disarmed
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait("Category", "integration")]
    public void AMissingOrCorruptFileDisarmsEverything()
    {
        Assert.Empty(SurfaceProfileStore.Load(
            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())));

        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(path, "not json");
        try { Assert.Empty(SurfaceProfileStore.Load(path)); }
        finally { File.Delete(path); }
    }

    // Regression: a fractional threshold or a non-object entry threw past the
    // JsonException catch and turned a tolerant load into a boot failure.
    [Fact]
    [Trait("Category", "integration")]
    public void AMalformedRecordIsSkippedNeverAThrow()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(path, """
            [
              "ChatGPT",
              { "process": "fractional", "pauseThresholdMs": 1.5 },
              { "process": "ChatGPT", "pauseThresholdMs": 2400 }
            ]
            """);
        try
        {
            var records = SurfaceProfileStore.Load(path);

            Assert.Equal(2, records.Count);
            Assert.Equal(0, records[0].PauseThresholdMs); // unreadable threshold, disarmed
            Assert.Equal(2400, records[1].PauseThresholdMs);
        }
        finally { File.Delete(path); }
    }

    // ── The provisional qualification formula (Lab side) ───────────────────

    private static SurfaceProfile Profile(
        int enter, int sentence, int timed, int p99) => new(
        "chat.exe", Sentences: enter + sentence, Words: 0,
        SentenceClosed: sentence, EnterClosed: enter, Interrupted: 0, OtherClosed: 0,
        TimedSentences: timed, Gaps: new GapStats(timed * 5, 200, 400, 800, p99, p99 * 2));

    [Fact]
    [Trait("Category", "unit")]
    public void AnEnterHeavySurfaceWithEnoughDataEarnsItsP99Bar()
    {
        Assert.Equal(2400, SurfaceProfiler.ProvisionalPauseThresholdMs(
            Profile(enter: 40, sentence: 10, timed: 45, p99: 2400)));
    }

    [Theory]
    [Trait("Category", "unit")]
    [InlineData(10, 40, 45)] // sentence-ender dominates — the closure arrives in time
    [InlineData(40, 10, 10)] // too few timed sentences to trust the statistics
    public void AnUnqualifiedSurfaceStaysDisarmed(int enter, int sentence, int timed)
    {
        Assert.Equal(0, SurfaceProfiler.ProvisionalPauseThresholdMs(
            Profile(enter, sentence, timed, p99: 2400)));
    }

    [Fact]
    [Trait("Category", "integration")]
    public void TheLiveJsonCarriesQualifiedSurfacesOnly()
    {
        var profiles = new[]
        {
            Profile(enter: 40, sentence: 10, timed: 45, p99: 2400),
            Profile(enter: 0, sentence: 50, timed: 50, p99: 900) with { Process = "word.exe" },
        };

        string json = SurfaceProfileReport.RenderLiveJson(profiles);
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(path, json);
        try
        {
            var records = SurfaceProfileStore.Load(path);
            var record = Assert.Single(records);
            Assert.Equal("chat.exe", record.Process);
            Assert.Equal(2400, record.PauseThresholdMs);
        }
        finally { File.Delete(path); }
    }
}
