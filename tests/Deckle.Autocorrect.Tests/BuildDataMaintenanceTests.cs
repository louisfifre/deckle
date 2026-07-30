using System.IO;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Lab;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The maintainer's build-data gesture, expressed as a test — Deckle keeps its
// offline data tooling in lib modules exercised by the suite, never a standalone
// CLI. This regenerates the versioned derived lexicons under
// src/Deckle.Autocorrect/Data/ (French, legacy English, verbs, and the restricted
// globish seed) from the raw sources fetched by scripts/commands/fetch-autocorrect-data.ps1,
// then self-certifies the globish seed.
//
// It is explicit and also skips unless the raw sources are present, so an ordinary
// test run never touches the repo. Run it deliberately after a fetch, with explicit
// tests enabled and a narrow method filter.
// LexiconBuilder is byte-deterministic, so a run over unchanged sources leaves the
// artifacts identical — `git diff` then shows exactly what a source update changed.
// The Morphalou overlay stays opt-in and out of this default gesture.
[Trait("Category", "maintenance")]
public sealed class BuildDataMaintenanceTests
{
    [Fact(Explicit = true)]
    public void RegenerateLexicons()
    {
        string repo = FindRepoRoot();
        string rawDir = Path.Combine(repo, "artifacts", "autocorrect-data", "raw");
        string outDir = Path.Combine(repo, "src", "Deckle.Autocorrect", "Data");

        Assert.SkipUnless(
            File.Exists(Path.Combine(rawDir, "Lexique383.tsv"))
                && File.Exists(Path.Combine(rawDir, "count_1w.txt"))
                && File.Exists(Path.Combine(rawDir, "FranceTerme.xml")),
            $"Raw autocorrect sources absent under {rawDir} — run scripts/commands/fetch-autocorrect-data.ps1 first.");

        int code = LexiconBuilder.Run(rawDir, outDir);
        Assert.Equal(0, code);

        // Self-certify the regenerated restricted globish seed: a known technical
        // term survives, a French-colliding stopword is filtered out (structural).
        var seed = AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(outDir);
        Assert.NotNull(seed);
        Assert.True(seed!.Contains("greenwashing"));
        Assert.False(seed.Contains("the"));
    }

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
