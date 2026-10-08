using Deckle.Llm.Rewrite;

namespace Deckle.Transcription;

// Every dictation applies the one persisted active profile while rewriting is
// enabled. No active profile keeps the raw result, regardless of any legacy
// auto-rule or shortcut-slot settings still present on disk.
internal static class RewriteProfileSelection
{
    public static RewriteProfile? ForDictation(LlmSettings settings)
    {
        if (!settings.Enabled || !HasActiveProfile(settings))
            return null;

        if (!string.IsNullOrEmpty(settings.ActiveProfileId))
        {
            RewriteProfile? byId = settings.Profiles.Find(profile =>
                profile.Id == settings.ActiveProfileId);
            if (byId is not null)
                return byId;
        }

        if (string.IsNullOrWhiteSpace(settings.ActiveProfileName))
            return null;

        return settings.Profiles.Find(profile =>
            string.Equals(
                profile.Name,
                settings.ActiveProfileName,
                StringComparison.OrdinalIgnoreCase));
    }

    // True when the settings name an active profile, whether or not it still
    // resolves — lets the caller tell "plain dictation" from "profile lost".
    public static bool HasActiveProfile(LlmSettings settings) =>
        !string.IsNullOrEmpty(settings.ActiveProfileId)
        || !string.IsNullOrWhiteSpace(settings.ActiveProfileName);
}
