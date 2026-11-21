using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Deckle.App.Tests;

// Why this test exists. Deckle.App is the published Deckle: the copy the
// maintainer's prod runs and the one a release would hand to other people.
// Mail and the Atelier are unpublished, so they ship as their own apps
// (Deckle.Mail.App, Deckle.Atelier.App) and the published build must not
// carry them. WinUI cannot load compiled XAML from an assembly the host did
// not reference at build time (microsoft/microsoft-ui-xaml#6299), so the only
// way one of them could reach the published app is a project reference, direct
// or through a library the app keeps. That second path is the one that went
// unseen: Deckle.Projects referenced Deckle.Atelier for months.
//
// Three rules, each failure naming every project at fault:
//   1. the recursive ProjectReference closure of Deckle.App.csproj (read from
//      the XML, no build) holds none of the unpublished projects;
//   2. the Deckle.deps.json the build produced lists none of them either, so
//      a package or a path the XML walk cannot see is caught too;
//   3. anchors: the closure does hold Deckle.Projects and Deckle.FolderMap
//      (proof that the walk reads something), and no src project but the two
//      app executables references Deckle.Mail.Hub or Deckle.Atelier.Views.
[Trait("Category", "integration")]
public sealed class PublishedClosureTests
{
    private static readonly string[] ForbiddenNames =
    [
        "Deckle.Mail", "Deckle.Mail.Hub", "Deckle.Mail.App",
        "Deckle.Atelier", "Deckle.Atelier.Views", "Deckle.Atelier.App",
    ];

    private static bool IsForbidden(string name)
        => ForbiddenNames.Contains(name, StringComparer.OrdinalIgnoreCase)
           || name.Contains("Playground", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void ThePublishedAppReferencesNoUnpublishedProjectDirectlyOrThroughAnother()
    {
        var closure = Closure();
        var offenders = closure.Where(kv => IsForbidden(kv.Key))
            .Select(kv => $"{kv.Key} (reached through {kv.Value})").ToList();

        Assert.True(offenders.Count == 0,
            "Deckle.App must not reference an unpublished project:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheBuiltDependencyListHoldsNoUnpublishedAssembly()
    {
        string depsPath = Path.Combine(AppContext.BaseDirectory, "Deckle.deps.json");
        Assert.True(File.Exists(depsPath), $"the host's Deckle.deps.json is not next to the tests: {depsPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(depsPath));
        var libraries = doc.RootElement.GetProperty("libraries").EnumerateObject()
            .Select(p => p.Name.Split('/')[0])
            .ToList();

        // Anchor: the list is not empty and holds a published project.
        Assert.Contains("Deckle.Projects", libraries);

        var offenders = libraries.Where(IsForbidden).ToList();
        Assert.True(offenders.Count == 0,
            "Deckle.deps.json lists an unpublished assembly:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheWalkReadsTheClosureAndOnlyTheAppExecutablesReferenceTheViewLibraries()
    {
        var closure = Closure();
        Assert.Contains("Deckle.Projects", closure.Keys);
        Assert.Contains("Deckle.FolderMap", closure.Keys);

        var viewLibraries = new[] { "Deckle.Mail.Hub", "Deckle.Atelier.Views" };
        var allowed = new[] { "Deckle.Mail.App", "Deckle.Atelier.App" };
        var offenders = new List<string>();
        foreach (string csproj in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories))
        {
            string owner = Path.GetFileNameWithoutExtension(csproj);
            if (allowed.Contains(owner, StringComparer.OrdinalIgnoreCase)) continue;
            foreach (string reference in References(csproj))
            {
                if (viewLibraries.Contains(reference, StringComparer.OrdinalIgnoreCase))
                    offenders.Add($"{owner} -> {reference}");
            }
        }

        Assert.True(offenders.Count == 0,
            "only the app executables may reference the view libraries of an unpublished app:\n  " + string.Join("\n  ", offenders));
    }

    // Every project reachable from Deckle.App.csproj, with the project that
    // pulled it in first (for the failure message).
    private static Dictionary<string, string> Closure()
    {
        string root = Path.Combine(RepoRoot(), "src", "Deckle.App", "Deckle.App.csproj");
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(root) };
        var queue = new Queue<string>();
        queue.Enqueue(Path.GetFullPath(root));

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            string currentName = Path.GetFileNameWithoutExtension(current);
            foreach (string include in IncludePaths(current))
            {
                string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, include.Replace('\\', Path.DirectorySeparatorChar)));
                string name = Path.GetFileNameWithoutExtension(target);
                if (!seen.ContainsKey(name)) seen[name] = currentName;
                if (File.Exists(target) && visited.Add(target)) queue.Enqueue(target);
            }
        }
        return seen;
    }

    private static IEnumerable<string> References(string csproj)
        => IncludePaths(csproj).Select(p => Path.GetFileNameWithoutExtension(p.Replace('\\', Path.DirectorySeparatorChar)));

    private static IEnumerable<string> IncludePaths(string csproj)
        => XDocument.Load(csproj).Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
