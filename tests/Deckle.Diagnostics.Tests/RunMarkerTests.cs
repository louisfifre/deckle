using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "integration")]
public sealed class RunMarkerTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"deckle-run-marker-{Guid.NewGuid():N}");

    [Fact]
    public void A_first_run_has_no_previous_run()
    {
        Assert.Null(RunMarker.Begin(_directory, "s1", 100, T0));
    }

    [Fact]
    public void A_run_that_started_and_never_ended_is_named_by_the_next_one()
    {
        RunMarker.Begin(_directory, "s1", 100, T0);

        RunMarker.PreviousRun? previous = RunMarker.Begin(_directory, "s2", 200, T0.AddMinutes(5));

        Assert.NotNull(previous);
        Assert.Equal("s1", previous.Session);
        Assert.Equal(100, previous.Pid);
        Assert.Equal(T0, previous.StartedAt);
    }

    [Fact]
    public void A_run_that_ended_in_order_leaves_nothing_for_the_next_one()
    {
        RunMarker.Begin(_directory, "s1", 100, T0);
        RunMarker.End(_directory, "s1");

        Assert.Null(RunMarker.Begin(_directory, "s2", 200, T0.AddMinutes(5)));
    }

    [Fact]
    public void Ending_twice_is_harmless()
    {
        RunMarker.Begin(_directory, "s1", 100, T0);
        RunMarker.End(_directory, "s1");

        RunMarker.End(_directory, "s1");

        Assert.Null(RunMarker.Begin(_directory, "s2", 200, T0));
    }

    [Fact]
    public void A_restarted_process_ending_late_does_not_erase_its_successor()
    {
        RunMarker.Begin(_directory, "old", 100, T0);
        RunMarker.End(_directory, "old");
        // The successor starts before the old process has finished exiting and
        // the old one ends its own run a second time.
        RunMarker.Begin(_directory, "new", 200, T0.AddSeconds(1));
        RunMarker.End(_directory, "old");

        RunMarker.PreviousRun? previous = RunMarker.Begin(_directory, "next", 300, T0.AddMinutes(1));

        Assert.Equal("new", previous?.Session);
    }

    [Fact]
    public void An_unreadable_marker_still_counts_as_an_unclean_end()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, RunMarker.FileName), "not a marker");

        RunMarker.PreviousRun? previous = RunMarker.Begin(_directory, "s2", 200, T0);

        Assert.NotNull(previous);
        Assert.Equal("unreadable", previous.Session);
        Assert.Null(previous.StartedAt);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
