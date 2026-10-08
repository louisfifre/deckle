using Deckle.Orchestration;
using Xunit;

namespace Deckle.Orchestration.Tests;

[Trait("Category", "unit")]
public sealed class ExecutableLocatorTests
{
    private const string Root = @"C:\Games\Hollow Knight";

    [Theory]
    [InlineData("unins000")]
    [InlineData("GameSetup")]
    [InlineData("UnityCrashHandler64")]
    [InlineData("vcredist_x64")]
    [InlineData("DXSETUP")]
    [InlineData("installer")]
    [InlineData("CrashReportClient")]
    public void HelpersAreNeverPicked(string helperStem)
    {
        var picked = ExecutableLocator.Pick(
            [(Exe(helperStem), 900_000_000), (Exe("game"), 1)],
            ["Hollow Knight"]);

        Assert.Equal(Exe("game"), picked);
    }

    [Fact]
    public void ExactStemBeatsPrefixAndPrefixBeatsSize()
    {
        string exact = Exe("hollow_knight");
        string prefix = Exe("HollowKnightLauncher");
        string largest = Exe("engine");

        Assert.Equal(exact, ExecutableLocator.Pick(
            [(largest, 500), (prefix, 200), (exact, 100)], ["Hollow Knight"]));
        Assert.Equal(prefix, ExecutableLocator.Pick(
            [(largest, 500), (prefix, 200)], ["Hollow Knight"]));
        Assert.Equal(largest, ExecutableLocator.Pick(
            [(Exe("tool"), 100), (largest, 500)], ["Hollow Knight"]));
    }

    [Fact]
    public void AnyPreferredStemCounts()
    {
        string byFolder = Exe("hk");

        Assert.Equal(byFolder, ExecutableLocator.Pick(
            [(Exe("other"), 500), (byFolder, 1)], ["Hollow Knight", "HK"]));
    }

    [Fact]
    public void NothingIsPickedWhenOnlyHelpersRemain()
    {
        Assert.Null(ExecutableLocator.Pick(
            [(Exe("unins000"), 10), (Exe("setup"), 20)], ["Hollow Knight"]));
        Assert.Null(ExecutableLocator.Pick([], ["Hollow Knight"]));
    }

    private static string Exe(string stem) => Path.Combine(Root, stem + ".exe");
}
