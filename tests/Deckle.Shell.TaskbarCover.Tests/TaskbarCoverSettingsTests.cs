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
    public void AHandEditedFigureOutOfRangeLandsOnItsNearestBound(int stored, int expected)
    {
        var settings = Read($$"""{ "approachDistanceDip": {{stored}} }""");

        Assert.Equal(expected, settings.ApproachDistanceDip);
    }

    [Fact]
    public void TheDefaultSitsInsideTheRange()
    {
        Assert.InRange(ApproachDistance.DefaultDip, ApproachDistance.MinimumDip, ApproachDistance.MaximumDip);
    }
}
