using Xunit;

namespace Deckle.Projects.Tests;

// The rows `menu.ps1 -List` prints, with synthetic ids: five tab-separated
// fields, one row per action and per variant.
[Trait("Category", "unit")]
public sealed class ScriptMenuParserTests
{
    private static string Row(string id, string label, string script, string arguments, string variant) =>
        string.Join('\t', id, label, script, arguments, variant);

    [Fact]
    public void VariantsAreGroupedUnderTheFirstRowsPlace()
    {
        string text = string.Join('\n',
            Row("build-run.1", "Build & run", "build-run.ps1", "", "Debug"),
            Row("self-check.0", "Self-check", "self-check.ps1", "", "Debug"),
            Row("self-check.0", "Self-check", "self-check.ps1", "", "Release"),
            Row("git.record-version.0", "Record a version", "git/record-version.ps1", "-Apply", "patch"),
            Row("git.record-version.0", "Record a version", "git/record-version.ps1", "-Apply", "minor"),
            Row("git.record-version.0", "Record a version", "git/record-version.ps1", "-Apply", "major"));

        IReadOnlyList<ScriptMenuEntry> entries = ScriptMenuParser.Parse(text);

        Assert.Equal(["build-run.1", "self-check.0", "git.record-version.0"], entries.Select(e => e.Id));
        Assert.Equal(["Debug", "Release"], entries[1].Variants);
        Assert.Equal(["patch", "minor", "major"], entries[2].Variants);
        Assert.Equal("git/record-version.ps1", entries[2].Script);
        Assert.Equal("-Apply", entries[2].Arguments);
    }

    [Fact]
    public void AnEntryWithoutAVariantHasNoVariantsAndKeepsItsArguments()
    {
        string text = Row("deploy-prod.1", "Roll back prod", "deploy-prod.ps1", "-Rollback", "");

        ScriptMenuEntry entry = ScriptMenuParser.Parse(text).Single();

        Assert.Empty(entry.Variants);
        Assert.Equal("-Rollback", entry.Arguments);
        Assert.Equal("Roll back prod", entry.Label);
    }

    [Fact]
    public void LabelsWithSlashesAndAmpersandsAndArgumentsWithQuotesArriveIntact()
    {
        string text = Row("clean.ai-sessions.0", "Clean / AI sessions", "clean.ps1", "-Apply -Confirmation 'DELETE IT'", "");

        ScriptMenuEntry entry = ScriptMenuParser.Parse(text).Single();

        Assert.Equal("Clean / AI sessions", entry.Label);
        Assert.Equal("-Apply -Confirmation 'DELETE IT'", entry.Arguments);
    }

    [Fact]
    public void BannerLinesAndATrailingBlankLineAreIgnored()
    {
        string text = "WARNING: something the shell said\n"
            + "\n"
            + Row("a.0", "A", "a.ps1", "", "") + "\n"
            + "only\ttwo fields\n"
            + "\t" + Row("", "No id", "x.ps1", "", "").TrimStart('\t') + "\n"
            + Row("b.0", "B", "b.ps1", "", "") + "\n\n";

        IReadOnlyList<ScriptMenuEntry> entries = ScriptMenuParser.Parse(text);

        Assert.Equal(["a.0", "b.0"], entries.Select(e => e.Id));
    }

    [Fact]
    public void WindowsLineEndingsAreTolerated()
    {
        string text = Row("a.0", "A", "a.ps1", "", "Debug") + "\r\n" + Row("a.0", "A", "a.ps1", "", "Release") + "\r\n";

        ScriptMenuEntry entry = ScriptMenuParser.Parse(text).Single();

        Assert.Equal(["Debug", "Release"], entry.Variants);
    }

    [Fact]
    public void NothingGivesNoEntry()
    {
        Assert.Empty(ScriptMenuParser.Parse(""));
        Assert.Empty(ScriptMenuParser.Parse("\n\n"));
    }

    [Fact]
    public void AnEntryGivesAPinWithTheVariantItWasPinnedWithOrTheDefault()
    {
        ScriptMenuEntry entry = ScriptMenuParser.Parse(
            Row("self-check.0", "Self-check", "self-check.ps1", "", "Debug") + "\n"
            + Row("self-check.0", "Self-check", "self-check.ps1", "", "Release")).Single();

        Assert.True(entry.Gives(new ActionRef("self-check.0", "Debug")));
        Assert.True(entry.Gives(new ActionRef("self-check.0", "Release")));
        Assert.True(entry.Gives(new ActionRef("self-check.0")));
        Assert.False(entry.Gives(new ActionRef("self-check.0", "Beta")));
        Assert.False(entry.Gives(new ActionRef("other.0", "Debug")));
    }
}
