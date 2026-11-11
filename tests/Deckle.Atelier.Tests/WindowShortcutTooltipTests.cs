using System.Xml.Linq;
using Xunit;

namespace Deckle.Atelier.Tests;

// The window's shortcuts (F5, Alt+Left, Ctrl+L) sit on a layout container.
// WinUI gives any element that carries accelerators a tooltip naming the first
// one and opens it when keyboard focus lands inside the element; on a
// container that is a stray "F5" under the window (WindowShortcutBugNotes.md).
// The views cannot be instantiated in a unit suite, so the markup is read.
[Trait("Category", "regression")]
public sealed class WindowShortcutTooltipTests
{
    private static readonly string[] Containers = ["Grid", "StackPanel", "Border", "Canvas", "RelativePanel"];

    [Fact]
    public void AContainerThatCarriesShortcutsShowsNoTooltipForThem()
    {
        string views = Path.Combine(RepoRoot(), "src", "Deckle.Atelier.Views");
        var failures = new List<string>();
        int carriers = 0;

        foreach (string file in Directory.EnumerateFiles(views, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            foreach (XElement element in XDocument.Load(file).Descendants())
            {
                string name = element.Name.LocalName;
                if (!Containers.Contains(name)) continue;
                if (!element.Elements().Any(e => e.Name.LocalName == $"{name}.KeyboardAccelerators")) continue;

                carriers++;
                if ((string?)element.Attribute("KeyboardAcceleratorPlacementMode") != "Hidden")
                    failures.Add($"{Path.GetFileName(file)}: <{name}> carries KeyboardAccelerators without KeyboardAcceleratorPlacementMode=\"Hidden\".");
            }
        }

        Assert.True(carriers > 0, "No container carries the window's shortcuts any more: this test guards nothing, move or remove it.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
