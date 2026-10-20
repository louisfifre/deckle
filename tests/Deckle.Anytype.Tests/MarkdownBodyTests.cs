using Deckle.Anytype;
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

    // ── TryReadSection: the raw content under a heading ─────────────────────────

    [Fact]
    public void TryReadSectionReturnsTheRawContentUpToTheNextHeadingOfTheSameLevel()
    {
        const string body = "## Un\nalpha\\_x   \n### sous\nbeta\n## Deux\nomega";

        Assert.True(MarkdownBody.TryReadSection(body, "un", out string content));
        // Raw: sub-headings stay, escapes and trailing spaces are not touched.
        Assert.Equal("alpha\\_x   \n### sous\nbeta", content);
    }

    [Fact]
    public void TryReadSectionIsFalseWhenNoHeadingMatches()
    {
        Assert.False(MarkdownBody.TryReadSection("## Un\nalpha", "Absent", out string content));
        Assert.Equal("", content);
    }

    // ── Preamble: what comes before the first heading ──────────────────────────

    [Fact]
    public void PreambleIsTheLinesBeforeTheFirstHeading()
    {
        Assert.Equal("Intro\n", MarkdownBody.Preamble("Intro\n\n## A\nx"));
    }

    [Fact]
    public void PreambleIsTheWholeBodyWhenItHasNoHeading()
    {
        // « #tag » without a space is text, not a heading.
        const string body = "Texte\n#tag ici";

        Assert.Equal(body, MarkdownBody.Preamble(body));
    }

    [Fact]
    public void PreambleIsEmptyWhenTheBodyOpensOnAHeading()
    {
        Assert.Equal("", MarkdownBody.Preamble("## A\nx"));
    }

    // ── Outline: headings with the size of their cleaned content ───────────────

    [Fact]
    public void OutlineListsEveryHeadingWithItsLevelAndCleanedContentSizeSubSectionsIncluded()
    {
        // « Titre » holds everything below it: intro, ## A, abc, ### A1, xy, ## B
        // once cleaned (5 + 4 + 3 + 6 + 2 + 4 chars, 5 line breaks, the divider
        // and the blank line dropped). « A » stops at « B » and keeps « A1 ».
        const string body = "# Titre\nintro\n## A\nabc\n### A1\nxy\n## B\n---\n";

        Assert.Equal(
            new[]
            {
                new MarkdownBody.Section(1, "Titre", 29),
                new MarkdownBody.Section(2, "A", 13),
                new MarkdownBody.Section(3, "A1", 2),
                new MarkdownBody.Section(2, "B", 0),
            },
            MarkdownBody.Outline(body));
    }

    [Fact]
    public void OutlineNormalizesHeadingsAndMeasuresUnescapedContent()
    {
        // The preamble « #tag line » is not a heading; the escaped underscore
        // counts as one character once cleaned (« corps_x » is 7).
        const string body = "#tag line\n## Entropy\\_thold   \ncorps\\_x";

        Assert.Equal(
            new[] { new MarkdownBody.Section(2, "Entropy_thold", 7) },
            MarkdownBody.Outline(body));
    }

    [Fact]
    public void OutlineOfABodyWithoutHeadingsIsEmpty()
    {
        Assert.Empty(MarkdownBody.Outline("Texte seul.\n- [ ] une case"));
    }

    // ── ChecklistItems: the subtasks of a body ─────────────────────────────────

    [Fact]
    public void ChecklistItemsReturnsTheItemsInOrderUnescapedAndSkipsEmptySlots()
    {
        // Markers - * + and an indent are accepted, [x] and [X] are both done;
        // « - [ ] » and « - [ ]» are the template's empty slot, « - pas… » and
        // plain text are not items.
        const string body =
            "- [x] fait\n  * [ ] module\\_X   \n+ [X] Majuscule\n- [ ] \n- [ ]\nTexte\n- pas une case";

        Assert.Equal(
            new[]
            {
                new MarkdownBody.ChecklistItem(true, "fait"),
                new MarkdownBody.ChecklistItem(false, "module_X"),
                new MarkdownBody.ChecklistItem(true, "Majuscule"),
            },
            MarkdownBody.ChecklistItems(body));
    }

    [Fact]
    public void ChecklistItemsOfABodyWithoutACheckboxIsEmpty()
    {
        Assert.Empty(MarkdownBody.ChecklistItems("- une puce\nTexte\n## Titre"));
    }

    [Fact]
    public void TryChecklistLineLocatesTheBoxCharacterAndTrimsTheLabel()
    {
        Assert.True(MarkdownBody.TryChecklistLine("  - [x]  libellé  ", out int box, out string label));
        Assert.Equal(5, box);
        Assert.Equal("libellé", label);

        Assert.False(MarkdownBody.TryChecklistLine("- [?] inconnu", out _, out _));
    }

    // ── Clean: body text as a reader wants it ──────────────────────────────────

    [Fact]
    public void CleanUnescapesDropsTrailingSpacesBlankLinesAndDividers()
    {
        // The escaped set is _ * ` | and the backslash itself; indentation is kept.
        const string raw = "a\\_b   \n\n---\n  \n---   \n  - c\\*d\\|e\\`f\\\\g  \nh";

        Assert.Equal("a_b\n  - c*d|e`f\\g\nh", MarkdownBody.Clean(raw));
    }

    [Fact]
    public void CleanOfOnlyLayoutIsEmpty()
    {
        Assert.Equal("", MarkdownBody.Clean("\n---\n   \n"));
    }

    [Fact]
    public void UnescapeLeavesABackslashThatEscapesNothingAlone()
    {
        Assert.Equal("module_X * \\q", MarkdownBody.Unescape("module\\_X \\* \\q"));
    }
}
