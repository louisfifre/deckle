using System.Xml.Linq;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests.Native;

// Why this test exists. A machine without the C++ build tools only got a
// warning (DECKLE001) and the build went on, so a Release build could be
// deployed without Deckle.Shell.Taskbar.Native.dll and the taskbar engine was
// silently unavailable in the installed app. A test cannot run MSBuild without
// the tools, so it holds the declaration: DECKLE001 is an Error in Release and
// stays a Warning elsewhere.
[Trait("Category", "regression")]
public sealed class NativeDllReleaseGuardTests
{
    [Fact]
    public void AReleaseBuildFailsWhenTheNativeDllCannotBeBuilt()
    {
        XDocument project = XDocument.Load(Path.Combine(RepoRoot(), "src", "Deckle.Shell.Taskbar", "Deckle.Shell.Taskbar.csproj"));

        XElement? error = project.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Error" && (string?)e.Attribute("Code") == "DECKLE001");
        XElement? warning = project.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Warning" && (string?)e.Attribute("Code") == "DECKLE001");

        Assert.NotNull(error);
        Assert.Contains("'$(Configuration)' == 'Release'", (string?)error!.Attribute("Condition"));
        Assert.NotNull(warning);
        Assert.Contains("'$(Configuration)' != 'Release'", (string?)warning!.Attribute("Condition"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
