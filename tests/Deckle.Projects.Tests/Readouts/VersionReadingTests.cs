using Xunit;

namespace Deckle.Projects.Tests;

// The rules the card and the flyout read the versions by (spec, Project page,
// Readouts), on sources given directly.
[Trait("Category", "unit")]
public sealed class VersionReadingTests
{
    private static VersionSource Tag(string value) => new(VersionSource.GitTagLabel, value);

    [Fact]
    public void TheMinecraftShapeOfP2GivesThreeVersionsThatDiffer()
    {
        var reading = new VersionReading("v0.32.0",
        [
            Tag("0.32.0"),
            new("launcher/app/Cargo.toml", "0.1.0"),
            new("mods/voice/gradle.properties", "0.1.0"),
        ]);

        Assert.Equal(3, reading.Count);
        Assert.True(reading.HasDifferingVersions);
        Assert.True(reading.ShowsCount);
        Assert.True(reading.ShowsDetail);
    }

    [Fact]
    public void ATagEqualToTheOnlyManifestGivesNoDetail()
    {
        var reading = new VersionReading("v0.6.0", [Tag("0.6.0"), new("Cargo.toml", "0.6.0")]);

        Assert.False(reading.HasDifferingVersions);
        Assert.False(reading.ShowsDetail);
        Assert.Equal(2, reading.Count);
    }

    [Fact]
    public void NoTagAndOneManifestShowsTheDetailWithOneSource()
    {
        var reading = new VersionReading(null, [new("package.json", "1.0.0")]);

        Assert.True(reading.ShowsDetail);
        Assert.False(reading.HasDifferingVersions);
        Assert.False(reading.ShowsCount);
        Assert.Equal(1, reading.Count);
    }

    [Fact]
    public void NoTagAndNoManifestShowsNothing()
    {
        Assert.False(VersionReading.None.ShowsDetail);
        Assert.False(VersionReading.None.HasDifferingVersions);
        Assert.Equal(0, VersionReading.None.Count);
    }

    [Fact]
    public void ATagWithoutManifestsGivesNoDetail()
    {
        var reading = new VersionReading("v1.0.0", [Tag("1.0.0")]);

        Assert.False(reading.ShowsDetail);
        Assert.Equal(1, reading.Count);
    }

    [Fact]
    public void CountIncludesTheSourcesTheCapLeftOut()
    {
        var reading = new VersionReading("v1.0.0", [Tag("1.0.0")], MoreSources: 10);

        Assert.Equal(11, reading.Count);
    }
}
