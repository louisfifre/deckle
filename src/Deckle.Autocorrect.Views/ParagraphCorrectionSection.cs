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
    public static bool BarIsShown(bool switchOn, bool needsSetup) => switchOn && needsSetup;
}
