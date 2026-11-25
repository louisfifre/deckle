using Deckle.Core;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

public enum ProfileUserKind
{
    /// <summary>A step of the dictation timeline rewrites with the profile.</summary>
    Dictation,

    /// <summary>The profile's own shortcut starts a rewrite.</summary>
    Shortcut,
}

/// <summary>One thing that uses a profile. The chord is set for a Shortcut.</summary>
public sealed record ProfileUser(ProfileUserKind Kind, HotkeyChord? Chord = null);

// Who uses each profile, read from what the settings persist, so the Rewriting
// page states it on the profile's card. Pure: the page only words the answer.
//
// The Transcribe and rewrite window is not listed: it remembers the profile
// last picked for the run only (TranscribeRewriteWindow), so no persisted
// choice says that it uses one.
public static class ProfileUsers
{
    public static IReadOnlyList<ProfileUser> Of(RewriteProfile profile, LlmSettings settings)
    {
        var users = new List<ProfileUser>();

        // A step counts only while "Rewrite dictations" is on: with it off, a
        // dictation arrives as said whatever the steps name.
        DictationRewriteSettings dictation = settings.Dictation;
        if (dictation.Enabled && dictation.Steps.Exists(step => step.ProfileId == profile.Id))
            users.Add(new ProfileUser(ProfileUserKind.Dictation));

        if (profile.HasActiveHotkey)
            users.Add(new ProfileUser(ProfileUserKind.Shortcut, profile.Hotkey));

        return users;
    }

    /// <summary>Whether paragraph correction sends to this provider: the one
    /// the probe asks, while paragraph correction is on.</summary>
    public static bool ParagraphCorrectionUses(
        LlmProvider provider, IReadOnlyList<LlmProvider> providers, bool paragraphCorrectionOn) =>
        paragraphCorrectionOn
        && ReferenceEquals(OllamaParagraphRewriteProbe.FindProvider(providers), provider);
}
