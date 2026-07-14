namespace Deckle.Llm.Rewrite;

// Neutral settings for a Rewrite module that is not present. The factory is
// deliberately lazy so composition never instantiates the module's settings
// service merely to discover that the module is absent.
public static class RewriteAvailability
{
    public static LlmSettings Settings(bool present, Func<LlmSettings> current) =>
        present
            ? current()
            : new LlmSettings { Enabled = false, Profiles = [] };
}
