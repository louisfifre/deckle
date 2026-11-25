namespace Deckle.Autocorrect.Views;

// What the Paragraph correction section of AutocorrectPage shows and when, kept
// apart from the page so a test can ask it.
public static class ParagraphCorrectionSection
{
    // The section exists only with the Rewrite module, and goes with the
    // Autocorrect switch like the page's other dependent cards: a declined
    // feature hides its dependents, it does not grey them.
    public static bool IsShown(bool rewritePresent, bool autocorrectOn) =>
        rewritePresent && autocorrectOn;

    // The bar states what is missing, only for a person who asked for the
    // correction: with the switch off, a missing model concerns nobody.
    public static bool BarIsShown(bool switchOn, ParagraphCorrectionSetup setup) =>
        switchOn && setup != ParagraphCorrectionSetup.None;

    // One text per cause: a title that names what is wrong, a message that says
    // how to get past it, and whether the bar leads to the Rewriting page. The
    // keys are in this module's resw; {0} is the pinned model.
    public static ParagraphBarText BarFor(ParagraphCorrectionSetup setup) => setup switch
    {
        ParagraphCorrectionSetup.OllamaNotRunning => new(
            "AutocorrectPage_ParagraphBar_OllamaNotRunning_Title",
            "AutocorrectPage_ParagraphBar_OllamaNotRunning_Message_Format",
            HasAction: false),
        ParagraphCorrectionSetup.ModelMissing => new(
            "AutocorrectPage_ParagraphBar_ModelMissing_Title_Format",
            "AutocorrectPage_ParagraphBar_ModelMissing_Message_Format",
            HasAction: false),
        ParagraphCorrectionSetup.ProviderOff => new(
            "AutocorrectPage_ParagraphBar_ProviderOff_Title",
            "AutocorrectPage_ParagraphBar_ProviderOff_Message",
            HasAction: true),
        _ => new(null, null, HasAction: false),
    };
}

// What the paragraph correction needs set up, as the page can tell it: the
// Rewrite module's availability, which this module does not reference, told in
// the page's own terms by the App.
public enum ParagraphCorrectionSetup
{
    /// <summary>Nothing to set up, or not known yet: the bar stays shut.</summary>
    None,
    OllamaNotRunning,
    ModelMissing,
    ProviderOff,
}

public readonly record struct ParagraphBarText(string? TitleKey, string? MessageKey, bool HasAction);
