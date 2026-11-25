using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// A start that fails because the bridge cannot stream says why on the
// Settings page (decided 2026-11-25: Ambient runs through an Entertainment
// area only, and when it cannot start it says the reason). The page reads
// AmbientEngine.LastStartFailure, which these mappings fill.
[Trait("Category", "unit")]
public sealed class AmbientStartFailureTests
{
    [Fact]
    public void AGroupWithNoEntertainmentAreaIsNamed()
    {
        var ex = new HueEntertainmentUnavailableException(
            HueEntertainmentUnavailableReason.NoEntertainmentArea, "no area");

        Assert.Equal(AmbientStartFailure.NoEntertainmentArea, AmbientStartFailures.From(ex));
        Assert.Equal("no_entertainment_area", AmbientStartFailures.LogReason(ex));
    }

    [Fact]
    public void ABridgeWithNoClientKeyIsNamed()
    {
        var ex = new HueEntertainmentUnavailableException(
            HueEntertainmentUnavailableReason.NoClientKey, "no key");

        Assert.Equal(AmbientStartFailure.NoClientKey, AmbientStartFailures.From(ex));
        Assert.Equal("no_client_key", AmbientStartFailures.LogReason(ex));
    }

    [Fact]
    public void AnyOtherFailureHasNoAdviceOfItsOwn()
    {
        var ex = new HttpRequestException("connection refused");

        Assert.Equal(AmbientStartFailure.Other, AmbientStartFailures.From(ex));
        Assert.Equal("exception", AmbientStartFailures.LogReason(ex));
    }
}
