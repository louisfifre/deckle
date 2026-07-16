using Deckle.Llm.Rewrite;

namespace Deckle.Transcription;

// Rewriting is an explicit hotkey action. The plain transcription hotkey
// supplies no requested profile and therefore always keeps the raw result,
// regardless of any legacy auto-rule settings still present on disk.
internal static class RewriteProfileSelection
{
    public static RewriteProfile? ForHotkey(
        LlmSettings settings,
        string? requestedProfileName)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(requestedProfileName))
            return null;

        return settings.Profiles.Find(profile =>
            string.Equals(
                profile.Name,
                requestedProfileName,
                StringComparison.OrdinalIgnoreCase));
    }
}
