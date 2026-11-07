using Xunit;

namespace Deckle.Hud.Tests;

// How Deckle's Animations setting and the Windows "Animation effects"
// preference combine (ADR-0009). Turning Windows's animations off once took
// every Deckle animation with it, the processing outline's rotation included,
// with no way to keep them (AnimationPreferenceBugNotes.md beside this file).
[Trait("Category", "unit")]
public sealed class AnimationPreferenceTests
{
    [Fact]
    [Trait("Category", "regression")]
    public void DeckleAnimatesWhenItsSettingIsOn_EvenWithWindowsAnimationsOff()
    {
        Assert.True(AnimationPreference.Resolve(AnimationPreference.ModeOn, windowsAnimationsEnabled: false));
    }

    [Fact]
    public void ReducedStopsDeckleAnimations_EvenWithWindowsAnimationsOn()
    {
        Assert.False(AnimationPreference.Resolve(AnimationPreference.ModeReduced, windowsAnimationsEnabled: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameAsWindowsFollowsWindows(bool windows)
    {
        Assert.Equal(windows, AnimationPreference.Resolve(AnimationPreference.ModeSystem, windows));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("on")]
    [InlineData("Fast")]
    public void AnUnknownValueFollowsWindows(string? mode)
    {
        Assert.True(AnimationPreference.Resolve(mode, windowsAnimationsEnabled: true));
        Assert.False(AnimationPreference.Resolve(mode, windowsAnimationsEnabled: false));
    }

    [Fact]
    public void TheStoredDefaultIsSameAsWindows()
    {
        Assert.Equal(AnimationPreference.ModeSystem, new Deckle.Settings.AppearanceSettings().Animations);
    }
}
