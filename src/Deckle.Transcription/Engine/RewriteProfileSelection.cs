using Deckle.Llm.Rewrite;

namespace Deckle.Transcription;

// Which profile rewrites a dictation:
//   1. a profile whose own shortcut started the take, whatever its length,
//      and even with rewriting switched off; the rest needs it on;
//   2. else, once the length is known and the length ladder is on, the
//      profile of the highest step the raw transcript reached;
//   3. else the active profile, which so acts as the default below the
//      first step. No active profile keeps the raw result: that is the floor.
// Legacy auto-rule, shortcut-slot and per-profile threshold settings still
// on disk are ignored.
internal static class RewriteProfileSelection
{
    // rawChars null = length not known yet (a batched take picks its profile
    // at start), so only the forced and the active profile apply.
    public static RewriteProfile? ForDictation(
        LlmSettings settings, int? rawChars = null, string? forcedProfileId = null)
    {
        // A profile's own shortcut is an explicit request for that rewrite, so
        // it holds even while automatic rewriting is switched off.
        if (!string.IsNullOrEmpty(forcedProfileId)
            && settings.Profiles.Find(profile => profile.Id == forcedProfileId) is { } forced)
            return forced;

        if (!settings.Enabled)
            return null;

        if (rawChars is { } chars && ByLength(settings, chars) is { } byLength)
            return byLength;

        return Active(settings);
    }

    // A step whose profile was deleted is skipped, so the step below it, or
    // the active profile, takes the dictation instead.
    public static RewriteProfile? ByLength(LlmSettings settings, int rawChars)
    {
        LengthSwitchSettings? ladder = settings.LengthSwitch;
        if (ladder is not { Enabled: true }) return null;

        RewriteProfile? best = null;
        int bestFrom = -1;
        foreach (LengthStep step in ladder.Steps)
        {
            if (rawChars < step.FromChars || step.FromChars <= bestFrom) continue;
            RewriteProfile? profile = settings.Profiles.Find(p => p.Id == step.ProfileId);
            if (profile is null) continue;
            best = profile;
            bestFrom = step.FromChars;
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
