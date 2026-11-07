using System.Text.Json;
using Deckle.Core;
using Deckle.Shell.TaskbarCover;
using Xunit;

namespace Deckle.Shell.TaskbarCover.Tests;

// The rule under test (2026-11-07): the approach distance is stored as a
// figure in device-independent pixels, and a file written while it was a
// named level reads as the DIP figure that level already stood for, so the
// cover reaches exactly as far as before the change. Read and written through
// the store's own JSON options.
[Trait("Category", "unit")]
public class TaskbarCoverSettingsTests
{
    private static TaskbarCoverSettings Read(string json)
        => JsonSerializer.Deserialize<TaskbarCoverSettings>(json, TaskbarCoverSettingsService.JsonOptions)!;

    private static string Write(TaskbarCoverSettings settings)
        => JsonSerializer.Serialize(settings, TaskbarCoverSettingsService.JsonOptions);

    // ── Migration of the named level ─────────────────────────────────────────

    [Theory]
    [InlineData("Near",     Spacing.Medium)]
    [InlineData("Standard", Spacing.Large)]
    [InlineData("Far",      Spacing.XLarge)]
    public void ANamedLevelReadsAsTheDipFigureItStoodFor(string level, int dip)
    {
        var settings = Read($$"""{ "enabled": true, "approach": "{{level}}" }""");

        Assert.Equal(dip, settings.ApproachDistanceDip);
    }

    [Theory]
    [InlineData(0, Spacing.Medium)]
    [InlineData(2, Spacing.XLarge)]
    // An ordinal outside the enum fell back to Standard in the old geometry.
    [InlineData(42, ApproachDistance.DefaultDip)]
    public void ALevelOrdinalFromAHandEditedFileReadsAsTheOldConverterDid(int ordinal, int dip)
    {
        var settings = Read($$"""{ "approach": {{ordinal}} }""");

        Assert.Equal(dip, settings.ApproachDistanceDip);
    }

    [Fact]
    public void AMigratedFileNoLongerWritesTheLevel()
    {
        var settings = Read("""{ "enabled": true, "approach": "Far" }""");

        string json = Write(settings);

        Assert.DoesNotContain("\"approach\"", json);
        Assert.Contains("\"approachDistanceDip\": 128", json);
    }

    [Fact]
    public void AMigratedFileReadsBackTheSameDistance()
    {
        var migrated = Read("""{ "approach": "Near" }""");

        var reread = Read(Write(migrated));

        Assert.Equal(Spacing.Medium, reread.ApproachDistanceDip);
    }

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

    // Regression: the card showed "px" beside a DIP figure, and the
    // maintainer read it as physical pixels ("still in pixels, not in DIP").
    [Trait("Category", "regression")]
    [Theory]
    [InlineData("px")]
    [InlineData("pixels")]
    public void TheUnitBesideTheFigureIsNeverAPixelWord(string pixelWord)
    {
        Assert.NotEqual(pixelWord, ApproachDistance.UnitLabel, ignoreCase: true);
        Assert.Equal("DIP", ApproachDistance.UnitLabel);
    }

    [Fact]
    public void TheDefaultSitsInsideTheRange()
    {
        Assert.InRange(ApproachDistance.DefaultDip, ApproachDistance.MinimumDip, ApproachDistance.MaximumDip);
    }
}
