using Deckle.Anytype.Gestures;
using Xunit;

namespace Deckle.Anytype.Tests;

// Unit tests for the pure body-edit engine. No I/O: these pin the section-splice
// behavior, the strict not-found / ambiguous outcomes, and the normalization that
// makes matching and read-after-write verification survive Anytype's escaping and
// hard-break reflow.
[Trait("Category", "unit")]
public class MarkdownBodyTests
{
    // ── ReplaceSection: splice keeps every other section verbatim ──────────────

    [Fact]
    public void ReplaceSectionRewritesOnlyTheTargetAndCopiesSiblingsVerbatim()
    {
        const string body = "# Tâche\n\n## État\nÀ faire.\n\n## Notes\nrien à signaler";

        MarkdownBody.SectionEdit edit = MarkdownBody.ReplaceSection(body, "État", "En cours.");

        Assert.Equal(MarkdownBody.EditStatus.Replaced, edit.Status);
        // The heading line stays, its body becomes the new content, and the
        // « Notes » section is untouched down to the byte.
        Assert.Equal("# Tâche\n\n## État\nEn cours.\n\n## Notes\nrien à signaler", edit.Body);
    }

    [Fact]
    public void ReplaceSectionAtEndOfDocumentRunsToEof()
    {
        const string body = "## Un\nalpha\n## Deux\nomega";

        MarkdownBody.SectionEdit edit = MarkdownBody.ReplaceSection(body, "Deux", "nouveau");

        Assert.Equal("## Un\nalpha\n## Deux\nnouveau", edit.Body);
    }

    [Fact]
    public void ReplaceSectionAbsorbsDeeperSubHeadingsButStopsAtSameLevel()
    {
        // Replacing the level-2 « Groupe » must swallow its level-3 child and stop
        // at the next level-2 heading.
        const string body = "## Groupe\n### enfant\ndétail\n## Suivant\nqueue";

        MarkdownBody.SectionEdit edit = MarkdownBody.ReplaceSection(body, "Groupe", "résumé");

        Assert.Equal("## Groupe\nrésumé\n## Suivant\nqueue", edit.Body);
    }

    [Fact]
    public void ReplaceSectionWithEmptyContentLeavesTheBareHeading()
    {
        const string body = "## A\nx\n## B\ny";

        MarkdownBody.SectionEdit edit = MarkdownBody.ReplaceSection(body, "A", "");

        Assert.Equal("## A\n## B\ny", edit.Body);
    }

    // ── ReplaceSection: matching is escape- and case-insensitive ───────────────

    [Fact]
    public void ReplaceSectionMatchesThroughAnytypeUnderscoreEscapingAndCase()
    {
        // The stored heading is escaped as Anytype re-exports it; the caller passes
        // the natural, unescaped, differently-cased form.
        const string body = "### entropy\\_thold   \nancien\n### autre\nz";

        MarkdownBody.SectionEdit edit = MarkdownBody.ReplaceSection(body, "ENTROPY_THOLD", "nouveau");

        Assert.Equal(MarkdownBody.EditStatus.Replaced, edit.Status);
        // The escaped heading line is preserved verbatim — only its body changed.
        Assert.Equal("### entropy\\_thold   \nnouveau\n### autre\nz", edit.Body);
    }

    // ── ReplaceSection: strict outcomes ────────────────────────────────────────

    [Fact]
    public void ReplaceSectionReturnsNotFoundWhenNoHeadingMatchesAndLeavesBodyUntouched()
    {
        const string body = "## Présent\ncorps";

        MarkdownBody.SectionEdit edit = MarkdownBody.ReplaceSection(body, "Absent", "x");

        Assert.Equal(MarkdownBody.EditStatus.NotFound, edit.Status);
        Assert.Equal(body, edit.Body);
    }

    [Fact]
    public void ReplaceSectionReturnsAmbiguousWhenTheHeadingTextRepeats()
    {
        const string body = "## Doublon\na\n## Doublon\nb";

        MarkdownBody.SectionEdit edit = MarkdownBody.ReplaceSection(body, "Doublon", "x");

        Assert.Equal(MarkdownBody.EditStatus.Ambiguous, edit.Status);
        Assert.Equal(2, edit.MatchCount);
        Assert.Equal(body, edit.Body);
    }

    [Fact]
    public void ReplaceSectionIgnoresNonHeadingHashes()
    {
        // "#tag" (no space) is not a heading, so it is not a match target.
        const string body = "regular #tag line\n## Vrai\ncorps";

        MarkdownBody.SectionEdit edit = MarkdownBody.ReplaceSection(body, "tag", "x");

        Assert.Equal(MarkdownBody.EditStatus.NotFound, edit.Status);
    }

    // ── SectionContentMatches: read-after-write intent check ───────────────────

    [Fact]
    public void SectionContentMatchesThroughEscapingAndTrailingWhitespace()
    {
        // The re-read body is Anytype's normalized render: underscore escaped, GFM
        // hard-break trailing spaces. The intent is the natural form.
        const string reread = "## État\nEn cours sur module\\_X.   \n## Fin\nz";

        Assert.True(MarkdownBody.SectionContentMatches(reread, "État", "En cours sur module_X."));
    }

    [Fact]
    public void SectionContentMatchesIgnoresBlankLineReflow()
    {
        const string reread = "## État\n\nligne une\n\nligne deux\n";

        Assert.True(MarkdownBody.SectionContentMatches(reread, "État", "ligne une\nligne deux"));
    }

    [Fact]
    public void SectionContentMatchesIsFalseOnContentDrift()
    {
        const string reread = "## État\nautre chose";

        Assert.False(MarkdownBody.SectionContentMatches(reread, "État", "ce que je voulais"));
    }

    [Fact]
    public void SectionContentMatchesIsFalseWhenTheHeadingVanished()
    {
        const string reread = "## Autre\nx";

        Assert.False(MarkdownBody.SectionContentMatches(reread, "État", "x"));
    }

    // ── HeadingTexts: the section-set guard source ─────────────────────────────

    [Fact]
    public void HeadingTextsListsEveryHeadingNormalizedInDocumentOrder()
    {
        const string body = "# Titre\ncorps\n## Groupe\\_un   \nx\n### sous\ny";

        Assert.Equal(
            new[] { "Titre", "Groupe_un", "sous" },
            MarkdownBody.HeadingTexts(body));
    }
}
