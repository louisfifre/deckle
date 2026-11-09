using System.Text.Json;
using Deckle.Core;
using Deckle.Shell.TaskbarCover;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// The rule under test (2026-11-07): the approach distance is stored as a
// figure in device-independent pixels. Read and written through the store's
// own JSON options.
[Trait("Category", "unit")]
public class TaskbarCoverSettingsTests
{
    private static TaskbarCoverSettings Read(string json)
        => JsonSerializer.Deserialize<TaskbarCoverSettings>(json, TaskbarCoverSettingsService.JsonOptions)!;

    // ── The DIP figure ───────────────────────────────────────────────────────

    [Fact]
    public void AFileWithoutTheSettingTakesTheDefault()
    {
        var settings = Read("""{ "enabled": true }""");

        Assert.Equal(ApproachDistance.DefaultDip, settings.ApproachDistanceDip);
        Assert.Equal(ApproachDistance.DefaultDip, new TaskbarCoverSettings().ApproachDistanceDip);
    }

    [Fact]
    public void ADipFigureReadsAsWritten()
    {
        var settings = Read("""{ "approachDistanceDip": 90 }""");

        Assert.Equal(90, settings.ApproachDistanceDip);
    }

    [Theory]
    [InlineData(0,     ApproachDistance.MinimumDip)]
    [InlineData(-40,   ApproachDistance.MinimumDip)]
    [InlineData(5000,  ApproachDistance.MaximumDip)]
    [InlineData(960,   960)]
    public void AHandEditedFigureOutOfRangeLandsOnItsNearestBound(int stored, int expected)
    {
        var settings = Read($$"""{ "approachDistanceDip": {{stored}} }""");

        Assert.Equal(expected, settings.ApproachDistanceDip);
    }

    // Regression: the card showed "px" beside a DIP figure, and the
    // maintainer read it as physical pixels ("still in pixels, not in DIP").
    [Trait("Category", "regression")]
    [Theory]
    [InlineData("px")]
    [InlineData("pixels")]
    public void TheUnitBesideTheFigureIsNeverAPixelWord(string pixelWord)
    {
        Assert.False(string.Equals(pixelWord, ApproachDistance.UnitLabel, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("DIP", ApproachDistance.UnitLabel);
    }

    [Theory]
    [InlineData(960,  960)]
    [InlineData(5,    ApproachDistance.MinimumDip * 2)]
    [InlineData(9999, ApproachDistance.MaximumDip)]
    public void TheSliderMaximumFollowsHalfTheScreenInsideTheRange(int halfScreenDip, int expected)
    {
        Assert.Equal(expected, ApproachDistance.SliderMaximum(halfScreenDip));
    }

    [Fact]
    public void TheDefaultSitsInsideTheRange()
    {
        Assert.InRange(ApproachDistance.DefaultDip, ApproachDistance.MinimumDip, ApproachDistance.MaximumDip);
    }
}
