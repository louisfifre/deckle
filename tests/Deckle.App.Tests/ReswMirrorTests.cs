using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Deckle.App.Tests;

// Why this test exists. A user-visible string read from code (Loc.Get,
// Loc.Format) resolves against the host app's resource map only, so every key
// a module reads that way lives twice: in the module's
// Strings/en-US/Resources.resw and in Deckle.App's. The comments in the App
// file declare the module copy the source of truth for wording and ask to
// "keep this mirror in sync", and nothing enforced it: three reviews of
// 2026-11-07 found copies that had drifted (a dialog describing what its
// buttons no longer did), and a module key read by code but absent from the
// App copy, which Release shows as an empty string.
//
// Two rules, each failure naming every key at fault:
//   1. a key present in a module copy and in the App copy has the same value;
//   2. a key a module or the App reads through Loc.Get / Loc.Format with a
//      literal name exists in the App copy.
//
// What it cannot see: keys built at run time (descriptor keys, "$"-strings,
// ternaries). Those stay the author's care. Keys read with Loc.GetFrom resolve
// against the module's own map and need no mirror, so they are not scanned.
[Trait("Category", "integration")]
public sealed class ReswMirrorTests
{
    private const string AppModule = "Deckle.App";

    private static readonly Regex LiteralLocCall = new(
        @"\bLoc\.(?:Get|Format)\(\s*""(?<key>[^""]+)""", RegexOptions.Compiled);

    [Fact]
    public void AKeyInBothCopiesHasTheSameValue()
    {
        var app = ReadResw(ReswPath(AppModule));
        var drifts = new List<string>();

        foreach ((string module, string path) in ModuleReswFiles())
        {
            var copy = ReadResw(path);
            foreach ((string key, string moduleValue) in copy)
            {
                if (app.TryGetValue(key, out string? appValue) && !string.Equals(appValue, moduleValue, StringComparison.Ordinal))
                {
                    drifts.Add($"{key} [{module}]\n    module: {moduleValue}\n    app:    {appValue}");
                }
            }
        }

        Assert.True(drifts.Count == 0,
            "These keys exist in a module's Resources.resw and in Deckle.App's with different values "
            + "(the module copy is the declared source of truth for wording; the app reads its own):\n"
            + string.Join("\n", drifts));
    }

    [Fact]
    public void AKeyReadThroughLocGetExistsInTheAppCopy()
    {
        var app = ReadResw(ReswPath(AppModule));
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (string file in SourceFiles())
        {
            foreach (string line in File.ReadLines(file))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;

                foreach (Match call in LiteralLocCall.Matches(line))
                {
                    string key = call.Groups["key"].Value;
                    if (!app.ContainsKey(key))
                    {
                        missing.Add($"{key}  (read in {Path.GetFileName(file)})");
                    }
                }
            }
        }

        Assert.True(missing.Count == 0,
            "These keys are read through Loc.Get / Loc.Format but are missing from "
            + "src/Deckle.App/Strings/en-US/Resources.resw, so Release shows an empty string. "
            + "Mirror them from the module's Resources.resw:\n" + string.Join("\n", missing));
    }

    private static Dictionary<string, string> ReadResw(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XElement data in XDocument.Load(path).Root!.Elements("data"))
        {
            values[(string)data.Attribute("name")!] = (string?)data.Element("value") ?? "";
        }
        return values;
    }

    private static string ReswPath(string module)
        => Path.Combine(RepoRoot(), "src", module, "Strings", "en-US", "Resources.resw");

    private static IEnumerable<(string Module, string Path)> ModuleReswFiles()
    {
        foreach (string dir in Directory.EnumerateDirectories(Path.Combine(RepoRoot(), "src")))
        {
            string module = Path.GetFileName(dir);
            if (module == AppModule) continue;
            string resw = ReswPath(module);
            if (File.Exists(resw)) yield return (module, resw);
        }
    }

    private static IEnumerable<string> SourceFiles()
    {
        string sep = Path.DirectorySeparatorChar.ToString();
        return Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(sep + "bin" + sep) && !f.Contains(sep + "obj" + sep));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
