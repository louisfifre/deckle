namespace Deckle.Llm.Rewrite.Tests;

// Deckle ships no rewrite profile, so a test that needs some sets them up
// here. The profiles carry no Id: LlmSettingsNormalizer fills it, as it does
// for a profile written by hand in the file.
internal static class LlmSettingsFixtures
{
    public static LlmSettings WithTwoProfiles() => new()
    {
        Profiles =
        [
            new RewriteProfile { Name = "Light", SystemPrompt = "Smooth the text." },
            new RewriteProfile { Name = "Structured", SystemPrompt = "Restructure the text." },
        ],
    };
}
