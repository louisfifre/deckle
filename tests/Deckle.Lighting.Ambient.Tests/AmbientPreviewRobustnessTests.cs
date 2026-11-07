using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// Louis, 2026-11-07 (v0.87.15): the Settings preview drew bands for lights he
// does not have, the room picker lost his choice, and after the scan the next
// Ambient Light start hung with no message. One regression test per cause.
[Trait("Category", "regression")]
public sealed class AmbientPreviewRobustnessTests
{
    // ── Zones: only the bands a light follows ────────────────────────────

    [Fact]
    public void OnlyTheZonesSomeLightFollowsAreDrawn()
    {
        // Louis's setup: one light on top, three on the sides, none below.
        var assignments = new Dictionary<string, LightZone>
        {
            ["3"] = LightZone.Top,
            ["4"] = LightZone.Left,
            ["5"] = LightZone.Right,
            ["6"] = LightZone.Left,
        };

        var used = AmbientPreviewZones.Used(assignments);

        Assert.Equal(3, used.Count);
        Assert.Contains(LightZone.Top, used);
        Assert.Contains(LightZone.Left, used);
        Assert.Contains(LightZone.Right, used);
        Assert.DoesNotContain(LightZone.Bottom, used);
    }

    [Fact]
    public void ALightSetToNoZoneDrawsNoBand()
    {
        var used = AmbientPreviewZones.Used(new Dictionary<string, LightZone> { ["3"] = LightZone.None });

        Assert.Empty(used);
    }

    [Fact]
    public void ALightOutsideTheRunDrawsNoBand()
    {
        var assignments = new Dictionary<string, LightZone>
        {
            ["3"] = LightZone.Top,
            ["9"] = LightZone.Bottom, // left over from another room
        };

        var used = AmbientPreviewZones.Used(assignments, runLightIds: ["3"]);

        Assert.Equal([LightZone.Top], used);
    }

    // ── Room picker: the stored choice is never replaced ─────────────────

    [Fact]
    public void TheStoredRoomIsTheOneSelected()
    {
        Assert.Equal(2, HueGroupSelection.IndexOf(["1", "4", "7"], "7"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("99")]
    public void NoStoredRoomOrOneTheBridgeDoesNotListSelectsNothing(string? stored)
    {
        // It used to select the first room, and the change handler saved it
        // over the stored id.
        Assert.Equal(-1, HueGroupSelection.IndexOf(["1", "4", "7"], stored));
    }

    // ── Scan release: a stuck release must not close the gate for good ───

    [Fact]
    public async Task AReleaseThatNeverEndsIsAbandonedAfterTheLimit()
    {
        var never = new TaskCompletionSource<Exception?>();

        var (completed, failure) = await AmbientScanRelease.RunBoundedAsync(
            () => never.Task, TimeSpan.FromMilliseconds(50));

        Assert.False(completed);
        Assert.Null(failure);
    }

    [Fact]
    public async Task AReleaseThatEndsInTimeReportsItsOwnFailure()
    {
        var boom = new InvalidOperationException("release");

        var (completed, failure) = await AmbientScanRelease.RunBoundedAsync(
            () => Task.FromResult<Exception?>(boom), TimeSpan.FromSeconds(5));

        Assert.True(completed);
        Assert.Same(boom, failure);
    }

    // ── Problem bar: a grey stage must say why ───────────────────────────

    [Fact]
    public void ACaptureWithNoFrameForThreeSecondsIsReported()
    {
        Assert.Equal(AmbientPreviewProblem.None,
            AmbientPreviewHealth.Classify(scanFailed: false, capturing: true, TimeSpan.FromSeconds(1)));
        Assert.Equal(AmbientPreviewProblem.NoFrames,
            AmbientPreviewHealth.Classify(scanFailed: false, capturing: true, AmbientPreviewHealth.NoFrameGrace));
    }

    [Fact]
    public void AScanThatFailedIsReportedWhateverTheFrames()
    {
        Assert.Equal(AmbientPreviewProblem.ScanFailed,
            AmbientPreviewHealth.Classify(scanFailed: true, capturing: false, TimeSpan.Zero));
    }

    [Fact]
    public void NoCaptureAndNoFailureIsNotAProblem()
    {
        Assert.Equal(AmbientPreviewProblem.None,
            AmbientPreviewHealth.Classify(scanFailed: false, capturing: false, TimeSpan.FromMinutes(5)));
    }
}
