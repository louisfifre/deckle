namespace Deckle.Llm.Rewrite;

// What the Autocorrect page tells a person about paragraph correction: whether
// the local Ollama or its model is missing, so it can say what to set up. Not
// while the PC has not been asked yet (Unknown) or can serve it.
public static class ParagraphRewriteNotices
{
    public static bool NeedsSetup(ParagraphRewriteAvailability availability) => availability switch
    {
        ParagraphRewriteAvailability.OllamaUnreachable => true,
        ParagraphRewriteAvailability.ModelMissing      => true,
        ParagraphRewriteAvailability.ProviderOff       => true,
        _                                              => false,
    };
}
