using Xunit;

namespace Deckle.Browser.Tests;

// Which downloads can run programs.
[Trait("Category", "unit")]
public sealed class DownloadSafetyTests
{
    [Theory]
    [InlineData("setup.exe")]
    [InlineData("SETUP.EXE")]
    [InlineData("a.msi")]
    [InlineData("a.msix")]
    [InlineData("a.appx")]
    [InlineData("a.bat")]
    [InlineData("a.cmd")]
    [InlineData("a.ps1")]
    [InlineData("a.vbs")]
    [InlineData("a.js")]
    [InlineData("a.jar")]
    [InlineData("a.scr")]
    [InlineData("a.com")]
    [InlineData("a.lnk")]
    [InlineData("a.reg")]
    [InlineData("a.hta")]
    [InlineData("a.dll")]
    [InlineData("report.pdf.exe")]
    [InlineData("tricky.exe.")]
    public void AProgramFileIsExecutable(string name)
    {
        Assert.True(DownloadSafety.IsExecutable(name));
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("photo.jpg")]
    [InlineData("archive.zip")]
    [InlineData("notes.txt")]
    [InlineData("exe")]
    [InlineData("")]
    [InlineData("a.json")]
    public void AnythingElseIsNot(string name)
    {
        Assert.False(DownloadSafety.IsExecutable(name));
    }
}
