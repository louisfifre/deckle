using Xunit;

namespace Deckle.Atelier.Tests;

// The pieces the map reads text with: the token estimate, the line count,
// the front matter and the hits of a document.
[Trait("Category", "unit")]
public sealed class MapTextTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("abcd", 1)]
    [InlineData("abcde", 2)]
    public void TokensAreCharactersDividedByFourRoundedUp(string text, int tokens) =>
        Assert.Equal(tokens, TokenEstimate.Of(text));

    [Theory]
    [InlineData("", 0)]
    [InlineData("one", 1)]
    [InlineData("one\n", 1)]
    [InlineData("one\r\ntwo", 2)]
    [InlineData("one\n\n", 2)]
    public void LinesCountAsAnEditorNumbersThem(string text, int lines) =>
        Assert.Equal(lines, TextLines.Count(text));

    [Fact]
    public void FrontMatterReadsPlainFoldedAndListValues()
    {
        FrontMatter front = FrontMatter.Parse("""
            ---
            name: "publier-version"
            description: >
              Monte la version
              et publie.
            paths:
              - "src/**"
            ---
            # Body
            """);

        Assert.Equal("publier-version", front["name"]);
        Assert.Equal("Monte la version et publie.", front["description"]);
        Assert.True(front.Has("paths"));
        Assert.False(front.Has("alwaysApply"));
    }

    [Fact]
    public void ATextWithoutClosingDashesHasNoFrontMatter() =>
        Assert.False(FrontMatter.Parse("---\nname: x\n# no end").Has("name"));

    [Fact]
    public void ImportsAndLinksAreReadOutsideCodeOnly()
    {
        List<MapHit> hits = [.. MapLinkReader.Read("""
            @AGENTS.md and [guide](docs/guide.md).
            `@README.md` stays literal, so does someone@example.com.
            ```
            @notes.md [x](inside.md)
            ```
            """, isScript: false, expandsImports: true)];

        Assert.Equal(["AGENTS.md"], hits.Where(h => h.Kind == MapHitKind.Import).Select(h => h.Value));
        Assert.Equal(["docs/guide.md"], hits.Where(h => h.Kind == MapHitKind.Link).Select(h => h.Value));
    }

    [Fact]
    public void ScriptsAndNamesAreReadInsideCodeToo()
    {
        List<MapHit> hits = [.. MapLinkReader.Read("""
            Run `deckle-bug-note`, then /publier-version.
            ```pwsh
            pwsh -File scripts/build.ps1 -Test
            ```
            It calls mcp__notes-locales__search.
            """, isScript: false, expandsImports: false)];

        Assert.Contains(hits, h => h is { Kind: MapHitKind.ScriptPath, Value: "scripts/build.ps1", Line: 3 });
        Assert.Contains(hits, h => h is { Kind: MapHitKind.Name, Value: "deckle-bug-note", Line: 1 });
        Assert.Contains(hits, h => h is { Kind: MapHitKind.Name, Value: "publier-version" });
        Assert.Contains(hits, h => h is { Kind: MapHitKind.Name, Value: "notes-locales", Line: 5 });
    }

    [Theory]
    [InlineData("https://example.com/a.md", null)]
    [InlineData("#section", null)]
    [InlineData("mailto:a@b.c", null)]
    [InlineData("docs/a%20b.md#part", "docs/a b.md")]
    public void ALinkTargetIsAPathInsideTheFolderOrNothing(string target, string? path) =>
        Assert.Equal(path, MapLinkReader.LinkPath(target));
}
