using Deckle.Lighting.Ambient;
using Deckle.Lighting.Hue;
using Xunit;

namespace Deckle.Tests.Lighting;

[Trait("Category", "regression")]
public class AmbientHueEchoClassifierTests
{
    [Fact]
    public void MatchingStateAfterOldTimestampWindowIsEcho()
    {
        var pushedAt = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var pushed = new AmbientHuePushedState(
            pushedAt,
            new HueProjectedState(
                On: true,
                Brightness: 42,
                Xy: (0.3127f, 0.3290f)));

        var update = new HueResourceUpdate(
            V2ResourceId: "v2-light",
            ResourceType: "light",
            CreationTime: pushedAt.AddSeconds(1),
            On: true,
            Brightness: 42,
            Xy: (0.3128f, 0.3291f));

        var decision = AmbientHueEchoClassifier.Classify(
            update,
            pushed,
            pushedAt.AddMilliseconds(1200));

        Assert.Equal(AmbientHueEventDecisionKind.Echo, decision.Kind);
    }

    [Fact]
    public void MatchingStateAfterEchoWindowIsExternal()
    {
        var pushedAt = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var pushed = new AmbientHuePushedState(
            pushedAt,
            new HueProjectedState(
                On: true,
                Brightness: 42,
                Xy: (0.3127f, 0.3290f)));

        var update = new HueResourceUpdate(
            V2ResourceId: "v2-light",
            ResourceType: "light",
            CreationTime: pushedAt.AddSeconds(3),
            On: true,
            Brightness: 42,
            Xy: (0.3127f, 0.3290f));

        var decision = AmbientHueEchoClassifier.Classify(
            update,
            pushed,
            pushedAt.AddMilliseconds(2500));

        Assert.Equal(AmbientHueEventDecisionKind.External, decision.Kind);
    }

    [Fact]
    public void OffEventIsEchoOnlyWhenLastPushedStateWasOff()
    {
        var pushedAt = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var update = new HueResourceUpdate(
            V2ResourceId: "v2-light",
            ResourceType: "light",
            CreationTime: pushedAt.AddMilliseconds(500),
            On: false,
            Brightness: null,
            Xy: null);

        var offPushed = new AmbientHuePushedState(
            pushedAt,
            new HueProjectedState(On: false, Brightness: null, Xy: null));
        var onPushed = new AmbientHuePushedState(
            pushedAt,
            new HueProjectedState(On: true, Brightness: 50, Xy: (0.3f, 0.3f)));

        var echo = AmbientHueEchoClassifier.Classify(
            update,
            offPushed,
            pushedAt.AddMilliseconds(500));
        var external = AmbientHueEchoClassifier.Classify(
            update,
            onPushed,
            pushedAt.AddMilliseconds(500));

        Assert.Equal(AmbientHueEventDecisionKind.Echo, echo.Kind);
        Assert.Equal(AmbientHueEventDecisionKind.External, external.Kind);
    }
}
