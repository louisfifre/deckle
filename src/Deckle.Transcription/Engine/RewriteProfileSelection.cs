using Deckle.Llm.Rewrite;

namespace Deckle.Transcription;

// Which profile rewrites a dictation:
//   1. a profile whose own shortcut started the take, whatever its length,
//      and even with rewriting switched off; the rest needs it on;
//   2. else, once the length is known, the auto-selected profile with the
//      highest threshold the take reached (on a tie, the first in the list);
//   3. else the active profile, which so acts as the default under every
//      threshold. No active profile keeps the raw result: that is the floor.
// Legacy auto-rule and shortcut-slot settings still on disk are ignored.
internal static class RewriteProfileSelection
{
    // sentAudioSec null = length not known yet (a batched take picks its
    // profile at start), so only the forced and the active profile apply.
    public static RewriteProfile? ForDictation(
        LlmSettings settings, double? sentAudioSec = null, string? forcedProfileId = null)
    {
        // A profile's own shortcut is an explicit request for that rewrite, so
        // it holds even while automatic rewriting is switched off.
        if (!string.IsNullOrEmpty(forcedProfileId)
            && settings.Profiles.Find(profile => profile.Id == forcedProfileId) is { } forced)
            return forced;

        if (!settings.Enabled)
            return null;

        if (sentAudioSec is { } seconds && ByLength(settings, seconds) is { } byLength)
            return byLength;

        return Active(settings);
    }

    public static RewriteProfile? ByLength(LlmSettings settings, double sentAudioSec)
    {
        RewriteProfile? best = null;
        foreach (RewriteProfile profile in settings.Profiles)
        {
            if (!profile.AutoSelect) continue;
            if (sentAudioSec < profile.AutoSelectFromMinutes * 60.0) continue;
            if (best is null || profile.AutoSelectFromMinutes > best.AutoSelectFromMinutes)
                best = profile;
        }
        return best;
    }

    private static RewriteProfile? Active(LlmSettings settings)
    {
        if (!HasActiveProfile(settings))
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
