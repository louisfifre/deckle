using Xunit;

namespace Deckle.Hud.Tests;

// When Deckle offers, once, its own animations: only to a person whose
// Windows animations are off and who never chose for Deckle (ADR-0009).
[Trait("Category", "unit")]
public sealed class AnimationOfferTests
{
    [Theory]
    [InlineData(AnimationPreference.ModeSystem)]
    [InlineData(null)]
    [InlineData("typed-by-hand")]
    public void FollowingWindowsWithItsAnimationsOff_TheOfferIsMade(string? mode)
    {
        Assert.True(AnimationPreference.ShouldOfferOwnAnimations(
            mode, windowsAnimationsEnabled: false, alreadyOffered: false));
    }

    [Fact]
    public void WithWindowsAnimationsOn_NoOfferIsMade()
    {
        Assert.False(AnimationPreference.ShouldOfferOwnAnimations(
            AnimationPreference.ModeSystem, windowsAnimationsEnabled: true, alreadyOffered: false));
    }

    [Theory]
    [InlineData(AnimationPreference.ModeOn)]
    [InlineData(AnimationPreference.ModeReduced)]
    public void APersonWhoAlreadyChose_GetsNoOffer(string mode)
    {
        Assert.False(AnimationPreference.ShouldOfferOwnAnimations(
            mode, windowsAnimationsEnabled: false, alreadyOffered: false));
    }

    [Fact]
    public void TheOfferIsMadeOnlyOnce()
    {
        Assert.False(AnimationPreference.ShouldOfferOwnAnimations(
            AnimationPreference.ModeSystem, windowsAnimationsEnabled: false, alreadyOffered: true));
    }
}
