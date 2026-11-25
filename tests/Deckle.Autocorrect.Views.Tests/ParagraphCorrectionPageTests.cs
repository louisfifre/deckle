using Deckle.Settings;
using Xunit;

namespace Deckle.Autocorrect.Views.Tests;

// What the Autocorrect page offers around paragraph correction: when its
// section shows, when its bar speaks, and that the settings search reaches the
// two cards the page gained. The module's flags are statics the App sets, so
// each test sets them and puts them back.
[Trait("Category", "unit")]
public sealed class ParagraphCorrectionPageTests : IDisposable
{
    private const string Page = "Deckle.Autocorrect.Views.AutocorrectPage, Deckle.Autocorrect.Views";

    public ParagraphCorrectionPageTests()
    {
        AutocorrectSettingsModule.ParagraphCorrectionPresent = true;
        AutocorrectSettingsModule.TranscriptionLexiconPresent = true;
        SettingsSearchIndex.RegisterPage(Page, "Deckle.Autocorrect.Views", "", "Autocorrect", SettingsSearch.Entries);
    }

    public void Dispose()
    {
        AutocorrectSettingsModule.ParagraphCorrectionPresent = false;
        AutocorrectSettingsModule.TranscriptionLexiconPresent = false;
        SettingsSearchIndex.RegisterPage(Page, "Deckle.Autocorrect.Views", "", "Autocorrect", []);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void TheSectionShowsOnlyWithTheRewriteModuleAndTheMasterOn(
        bool rewritePresent, bool autocorrectOn, bool expected)
    {
        Assert.Equal(expected, ParagraphCorrectionSection.IsShown(rewritePresent, autocorrectOn));
    }

    [Theory]
    [InlineData(true, ParagraphCorrectionSetup.OllamaNotRunning, true)]
    [InlineData(true, ParagraphCorrectionSetup.ModelMissing, true)]
    [InlineData(true, ParagraphCorrectionSetup.ProviderOff, true)]
    [InlineData(true, ParagraphCorrectionSetup.None, false)]
    [InlineData(false, ParagraphCorrectionSetup.OllamaNotRunning, false)]
    [InlineData(false, ParagraphCorrectionSetup.ModelMissing, false)]
    [InlineData(false, ParagraphCorrectionSetup.ProviderOff, false)]
    [InlineData(false, ParagraphCorrectionSetup.None, false)]
    public void TheBarSpeaksOnlyWhileTheSwitchIsOnAndSomethingIsMissing(
        bool switchOn, ParagraphCorrectionSetup setup, bool expected)
    {
        Assert.Equal(expected, ParagraphCorrectionSection.BarIsShown(switchOn, setup));
    }

    [Theory]
    [InlineData(ParagraphCorrectionSetup.OllamaNotRunning, "AutocorrectPage_ParagraphBar_OllamaNotRunning_Title", "AutocorrectPage_ParagraphBar_OllamaNotRunning_Message_Format", false)]
    [InlineData(ParagraphCorrectionSetup.ModelMissing, "AutocorrectPage_ParagraphBar_ModelMissing_Title_Format", "AutocorrectPage_ParagraphBar_ModelMissing_Message_Format", false)]
    [InlineData(ParagraphCorrectionSetup.ProviderOff, "AutocorrectPage_ParagraphBar_ProviderOff_Title", "AutocorrectPage_ParagraphBar_ProviderOff_Message", true)]
    public void EachCauseHasItsOwnTitleAndMessageAndOnlyTheProviderOffLeadsToRewriting(
        ParagraphCorrectionSetup setup, string titleKey, string messageKey, bool hasAction)
    {
        ParagraphBarText text = ParagraphCorrectionSection.BarFor(setup);

        Assert.Equal(titleKey, text.TitleKey);
        Assert.Equal(messageKey, text.MessageKey);
        Assert.Equal(hasAction, text.HasAction);
    }

    [Fact]
    public void WithNothingToSetUpThereIsNoText()
    {
        ParagraphBarText text = ParagraphCorrectionSection.BarFor(ParagraphCorrectionSetup.None);

        Assert.Null(text.TitleKey);
        Assert.Null(text.MessageKey);
        Assert.False(text.HasAction);
    }

    [Theory]
    [InlineData("paragraph")]
    [InlineData("shift+enter")]
    [InlineData("ollama")]
    [InlineData("correction")]
    public void TheSearchFindsTheParagraphSwitchOnTheAutocorrectPage(string query)
    {
        var (hits, _) = SettingsSearchIndex.Search(query, max: 10);

        SettingSearchHit hit = Assert.Single(hits, h => h.CardTag == "AutocorrectPage_ParagraphCard");
        Assert.Equal(Page, hit.PageTag);
        Assert.Equal("Correct a paragraph with Shift+Enter", hit.Label);
    }

    [Fact]
    public void TheSearchFindsLexiconByLexicon()
    {
        var (hits, _) = SettingsSearchIndex.Search("lexicon", max: 10);

        SettingSearchHit hit = Assert.Single(hits, h => h.CardTag == "AutocorrectPage_DeclaredWordsCard");
        Assert.Equal(Page, hit.PageTag);
        Assert.Equal("Lexicon", hit.Label);
    }

    [Fact]
    public void TheCardsAreNotSearchableWhereTheirModuleIsAbsent()
    {
        AutocorrectSettingsModule.ParagraphCorrectionPresent = false;
        AutocorrectSettingsModule.TranscriptionLexiconPresent = false;
        SettingsSearchIndex.RegisterPage(Page, "Deckle.Autocorrect.Views", "", "Autocorrect", SettingsSearch.Entries);

        var (paragraph, _) = SettingsSearchIndex.Search("shift+enter", max: 10);
        var (words, _) = SettingsSearchIndex.Search("lexicon", max: 10);

        Assert.DoesNotContain(paragraph, h => h.CardTag == "AutocorrectPage_ParagraphCard");
        Assert.DoesNotContain(words, h => h.CardTag == "AutocorrectPage_DeclaredWordsCard");
    }
}
