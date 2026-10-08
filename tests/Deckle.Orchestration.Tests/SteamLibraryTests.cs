using Deckle.Orchestration;
using Xunit;

namespace Deckle.Orchestration.Tests;

[Trait("Category", "unit")]
public sealed class SteamLibraryTests
{
    [Fact]
    public void ReadLibraryPathsReturnsEveryLibraryInFileOrder()
    {
        var paths = SteamLibrary.ReadLibraryPaths(Fixture.Read("libraryfolders.vdf"));

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], paths);
    }

    [Fact]
    public void ReadLibraryPathsDropsRepeatsAndReadsTheOldFlatForm()
    {
        const string text = """
            "LibraryFolders"
            {
                "TimeNextStatsReport" "1700000000"
                "1" "E:\\Games\\Steam"
                "2" { "path" "e:\\games\\steam\\" }
            }
            """;

        Assert.Equal([@"E:\Games\Steam"], SteamLibrary.ReadLibraryPaths(text));
    }

    [Fact]
    public void ReadManifestMapsTheGameAndResolvesItsFolder()
    {
        var manifest = SteamLibrary.ReadManifest(Fixture.Read("appmanifest_480.acf"), @"D:\SteamLibrary");

        Assert.NotNull(manifest);
        Assert.Equal("480", manifest.AppId);
        Assert.Equal("Spacewar", manifest.Name);
        Assert.Equal("Spacewar", manifest.InstallDir);
        Assert.Equal(@"D:\SteamLibrary\steamapps\common\Spacewar", manifest.InstallDirectory);
    }

    [Fact]
    public void ReadManifestReturnsNullWithoutAnInstallFolder()
    {
        const string text = "\"AppState\" { \"appid\" \"480\" \"name\" \"Spacewar\" }";

        Assert.Null(SteamLibrary.ReadManifest(text, @"D:\SteamLibrary"));
    }
}
