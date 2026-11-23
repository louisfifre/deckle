namespace Deckle.Llm.Rewrite;

// What the Rewriting page tells a person about paragraph rewriting: nothing
// while the module is off or the PC can serve it (or has not been asked yet),
// otherwise what is missing. The page maps the answer to its own text.
public enum ParagraphRewriteNotice
{
    None,
    OllamaMissing,
    ModelMissing,
    ProviderOff,
}

public static class ParagraphRewriteNotices
{
    public static ParagraphRewriteNotice For(bool rewritingEnabled, ParagraphRewriteAvailability availability)
    {
        if (!rewritingEnabled) return ParagraphRewriteNotice.None;

        return availability switch
        {
            ParagraphRewriteAvailability.OllamaUnreachable => ParagraphRewriteNotice.OllamaMissing,
            ParagraphRewriteAvailability.ModelMissing      => ParagraphRewriteNotice.ModelMissing,
            ParagraphRewriteAvailability.ProviderOff       => ParagraphRewriteNotice.ProviderOff,
            _                                              => ParagraphRewriteNotice.None,
        };
    }
}
