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
        var codeKeys = setup.Where(entry => !entry.Key.Contains('.')).ToList();

        // The guard is empty-proof: no code key at all would make the drift
        // check below pass without having compared anything.
        Assert.NotEmpty(codeKeys);

        var drift = codeKeys
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
        // A guard that cannot find the tree it guards must fail, not skip: a skip
        // would let the drift it exists to catch go through unseen.
        Assert.NotNull(dir);
        return dir.FullName;
    }
}
