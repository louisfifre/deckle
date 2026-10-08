using Deckle.Orchestration;
using Xunit;

namespace Deckle.Orchestration.Tests;

[Trait("Category", "unit")]
public sealed class AppMatcherTests
{
    private const string GameDirectory = @"C:\Games\Hollow";
    private const string GameExe = @"C:\Games\Hollow\Hollow.exe";

    [Theory]
    [InlineData(@"C:\Games\Hollow\Hollow.exe", "Hollow")]
    [InlineData("hollow.EXE", "hollow")]
    [InlineData("hollow", "hollow")]
    [InlineData("Foo.Launcher.exe", "Foo.Launcher")]
    [InlineData("C:/Games/Hollow/Hollow.exe", "Hollow")]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    public void NormalizeProcessNameStripsDirectoryAndExeExtension(string? input, string expected)
    {
        Assert.Equal(expected, AppMatcher.NormalizeProcessName(input));
    }

    [Fact]
    public void ExecutablePathBeatsInstallDirectoryAndProcessName()
    {
        var byName = Entry(processName: "Hollow");
        var byDirectory = Entry(processName: "other", installDirectory: GameDirectory);
        var byPath = Entry(processName: "third", executablePath: GameExe);

        var match = AppMatcher.Match([byName, byDirectory, byPath], "Hollow", GameExe);

        Assert.Same(byPath, match);
    }

    [Fact]
    public void InstallDirectoryBeatsProcessName()
    {
        var byName = Entry(processName: "Hollow");
        var byDirectory = Entry(processName: "other", installDirectory: GameDirectory);

        var match = AppMatcher.Match([byName, byDirectory], "Hollow", @"C:\Games\Hollow\bin\Hollow.exe");

        Assert.Same(byDirectory, match);
    }

    [Fact]
    public void ProcessNameMatchesWhenThePathIsUnknown()
    {
        var byName = Entry(processName: "Hollow");

        Assert.Same(byName, AppMatcher.Match([byName], "hollow.exe", executablePath: null));
    }

    [Fact]
    public void MatchingIgnoresCase()
    {
        var byPath = Entry(processName: "x", executablePath: GameExe);
        var byDirectory = Entry(processName: "y", installDirectory: @"D:\Steam\Celeste");
        var byName = Entry(processName: "NOTEPAD");

        Assert.Same(byPath, AppMatcher.Match([byPath], null, GameExe.ToUpperInvariant()));
        Assert.Same(byDirectory, AppMatcher.Match([byDirectory], null, @"d:\steam\celeste\Celeste.exe"));
        Assert.Same(byName, AppMatcher.Match([byName], "notepad", null));
    }

    [Fact]
    public void SiblingFolderWithACommonPrefixIsNotUnderTheInstallDirectory()
    {
        var byDirectory = Entry(processName: "x", installDirectory: GameDirectory);

        Assert.Null(AppMatcher.Match([byDirectory], "y", @"C:\Games\HollowKnight\Game.exe"));
    }

    [Fact]
    public void DisabledEntriesNeverMatch()
    {
        var disabled = Entry(processName: "Hollow", executablePath: GameExe, installDirectory: GameDirectory);
        disabled.Enabled = false;

        Assert.Null(AppMatcher.Match([disabled], "Hollow", GameExe));
    }

    [Fact]
    public void SameExecutableIsADuplicate()
    {
        var existing = Entry(processName: "Hollow", executablePath: GameExe);
        var candidate = Entry(processName: "renamed", executablePath: GameExe.ToLowerInvariant());

        Assert.True(AppMatcher.IsDuplicate([existing], candidate));
    }

    [Fact]
    public void SameInstallDirectoryIsADuplicate()
    {
        var existing = Entry(processName: "Hollow", installDirectory: GameDirectory + @"\");
        var candidate = Entry(processName: "other", installDirectory: GameDirectory.ToUpperInvariant());

        Assert.True(AppMatcher.IsDuplicate([existing], candidate));
    }

    [Fact]
    public void SameProcessNameAloneIsNotADuplicate()
    {
        var existing = Entry(processName: "game", executablePath: @"C:\Games\A\game.exe");
        var candidate = Entry(processName: "game", executablePath: @"C:\Games\B\game.exe");

        Assert.False(AppMatcher.IsDuplicate([existing], candidate));
    }

    private static AppEntry Entry(
        string processName, string? executablePath = null, string? installDirectory = null) => new()
    {
        DisplayName = processName,
        ProcessName = processName,
        ExecutablePath = executablePath,
        InstallDirectory = installDirectory,
    };
}
