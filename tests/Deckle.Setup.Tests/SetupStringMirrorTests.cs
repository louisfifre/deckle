using System.Xml.Linq;
using Xunit;

namespace Deckle.Setup.Tests;

// Loc.Get reads only the app's root resource map, so every code key the setup
// module keeps in its own .resw needs an identical copy in Deckle.App's. A
// missing copy still compiles, then shows "[!key]" in Debug and an empty label
// in Release. See LocMirrorBugNotes.md.
public sealed class SetupStringMirrorTests
{
    [Fact]
    [Trait("Category", "regression")]
    public void Every_setup_code_key_has_an_identical_app_mirror()
    {
        string repo = FindRepoRoot();
        var setup = ReadEntries(Path.Combine(repo, "src", "Deckle.Setup", "Strings", "en-US", "Resources.resw"));
        var app   = ReadEntries(Path.Combine(repo, "src", "Deckle.App", "Strings", "en-US", "Resources.resw"));

        // x:Uid keys carry a property after a dot and resolve against the
        // module's own map; only the dotless code keys go through Loc.Get.
        var drift = setup
            .Where(entry => !entry.Key.Contains('.'))
            .Where(entry => !app.TryGetValue(entry.Key, out string? mirror) || mirror != entry.Value)
            .Select(entry => entry.Key)
            .ToList();

        Assert.Empty(drift);
    }

    private static Dictionary<string, string> ReadEntries(string path) =>
        XDocument.Load(path).Root!
            .Elements("data")
            .ToDictionary(
                data => (string)data.Attribute("name")!,
                data => (string?)data.Element("value") ?? "",
                StringComparer.Ordinal);

    // Walk up from the test binary to the repo root, marked by the central package
    // props that live only there — worktree-safe (no hardcoded path).
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        Assert.SkipWhen(dir is null, "Could not locate the repo root (Directory.Packages.props).");
        return dir!.FullName;
    }
}
