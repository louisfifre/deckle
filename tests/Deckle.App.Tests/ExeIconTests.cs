using System.Xml.Linq;
using Xunit;

namespace Deckle.App.Tests;

// Why this test exists. Deckle.exe shipped without an icon of its own: every
// window set the mark with AppWindow.SetIcon, but the taskbar group, a pinned
// button, the Start shortcut and Explorer read the exe's resource, and showed
// the generic application icon on every window (seen 2026-11-20). The exe's
// icon comes from <ApplicationIcon> in Deckle.App.csproj; this guards that it
// is there and names the window icon at rest.
[Trait("Category", "integration")]
public sealed class ExeIconTests
{
    private const string WindowIconAtRest = "deckle--state--rest.ico";

    [Fact]
    public void TheExeEmbedsTheWindowIconAtRest()
    {
        string projectDir = Path.Combine(RepoRoot(), "src", "Deckle.App");
        string? icon = XDocument.Load(Path.Combine(projectDir, "Deckle.App.csproj"))
            .Descendants("ApplicationIcon").Select(e => e.Value.Trim()).SingleOrDefault();

        Assert.NotNull(icon);
        Assert.Equal(WindowIconAtRest, Path.GetFileName(icon));
        Assert.True(File.Exists(Path.Combine(projectDir, icon)), $"{icon} is not in Deckle.App");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
