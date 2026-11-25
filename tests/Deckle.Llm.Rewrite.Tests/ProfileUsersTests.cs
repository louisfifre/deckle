using Deckle.Core;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// Who uses a profile, as the Rewriting page states it on the profile's card,
// and which provider paragraph correction also uses.
[Trait("Category", "unit")]
public sealed class ProfileUsersTests
{
    private static readonly HotkeyChord Chord = new(HotkeyModifiers.Control | HotkeyModifiers.Win, 0x29);

    private static (LlmSettings Settings, RewriteProfile Profile, RewriteProfile Other) Setup()
    {
        LlmSettings settings = LlmSettingsFixtures.WithTwoProfiles();
        LlmSettingsNormalizer.Apply(settings);
        settings.Dictation.Enabled = true;
        return (settings, settings.Profiles[0], settings.Profiles[1]);
    }

    [Fact]
    public void AProfileOfADictationStepIsUsedByTheDictation()
    {
        var (settings, profile, other) = Setup();
        settings.Dictation.Steps.Clear();
        settings.Dictation.Steps.Add(new RewriteStep { ProfileId = profile.Id });

        Assert.Equal([new ProfileUser(ProfileUserKind.Dictation)], ProfileUsers.Of(profile, settings));
        Assert.Empty(ProfileUsers.Of(other, settings));
    }

    [Fact]
    public void WithDictationRewritingOffNoStepUsesItsProfile()
    {
        var (settings, profile, _) = Setup();
        settings.Dictation.Steps.Clear();
        settings.Dictation.Steps.Add(new RewriteStep { ProfileId = profile.Id });
        settings.Dictation.Enabled = false;

        Assert.Empty(ProfileUsers.Of(profile, settings));
    }

    [Fact]
    public void AProfileWithAShortcutIsUsedByItsChord()
    {
        var (settings, profile, other) = Setup();
        settings.Dictation.Steps.Clear();
        settings.Dictation.Steps.Add(new RewriteStep { ProfileId = other.Id });
        profile.HotkeyEnabled = true;
        profile.Hotkey = Chord;

        Assert.Equal([new ProfileUser(ProfileUserKind.Shortcut, Chord)], ProfileUsers.Of(profile, settings));
    }

    [Fact]
    public void AShortcutSwitchedOffUsesNothingAndTheChordIsKept()
    {
        var (settings, profile, other) = Setup();
        settings.Dictation.Steps.Clear();
        settings.Dictation.Steps.Add(new RewriteStep { ProfileId = other.Id });
        profile.HotkeyEnabled = false;
        profile.Hotkey = Chord;

        Assert.Empty(ProfileUsers.Of(profile, settings));
    }

    [Fact]
    public void ADictationStepAndAShortcutBothShowDictationFirst()
    {
        var (settings, profile, _) = Setup();
        settings.Dictation.Steps.Clear();
        settings.Dictation.Steps.Add(new RewriteStep { ProfileId = profile.Id });
        profile.HotkeyEnabled = true;
        profile.Hotkey = Chord;

        Assert.Equal(
            [new ProfileUser(ProfileUserKind.Dictation), new ProfileUser(ProfileUserKind.Shortcut, Chord)],
            ProfileUsers.Of(profile, settings));
    }

    [Fact]
    public void AProfileNobodyUsesHasNoUsers()
    {
        var (settings, profile, other) = Setup();
        settings.Dictation.Steps.Clear();
        settings.Dictation.Steps.Add(new RewriteStep { ProfileId = other.Id });

        Assert.Empty(ProfileUsers.Of(profile, settings));
    }

    // ── The provider paragraph correction also uses ─────────────────────

    [Fact]
    public void TheOllamaProviderIsUsedByParagraphCorrectionWhileItIsOn()
    {
        LlmSettings settings = new();
        LlmProvider ollama = settings.Providers.Single(p => p.Id == ParagraphRewrite.ProviderId);
        LlmProvider other = settings.Providers.Single(p => p.Id == "claude");

        Assert.True(ProfileUsers.ParagraphCorrectionUses(ollama, settings.Providers, paragraphCorrectionOn: true));
        Assert.False(ProfileUsers.ParagraphCorrectionUses(ollama, settings.Providers, paragraphCorrectionOn: false));
        Assert.False(ProfileUsers.ParagraphCorrectionUses(other, settings.Providers, paragraphCorrectionOn: true));
    }

    [Fact]
    public void ASwitchedOffOllamaProviderIsNotUsedByParagraphCorrection()
    {
        LlmSettings settings = new();
        LlmProvider ollama = settings.Providers.Single(p => p.Id == ParagraphRewrite.ProviderId);
        ollama.Enabled = false;

        Assert.False(ProfileUsers.ParagraphCorrectionUses(ollama, settings.Providers, true));
    }
}
