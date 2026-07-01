using Deckle.Lighting.Ambient;
using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "regression")]
public class AmbientHueEchoClassifierTests
{
    [Fact]
    public void RecentMatchingStateIsEcho()
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
    public void MatchingStateBeyondWindowIsStillEcho()
    {
        var pushedAt = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var pushed = new AmbientHuePushedState(
            pushedAt,
            new HueProjectedState(
                On: true,
                Brightness: 42,
                Xy: (0.3127f, 0.3290f)));

        // Same state coming back well past the former 2 s window — our own
        // echo, must not be read as external.
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

        Assert.Equal(AmbientHueEventDecisionKind.Echo, decision.Kind);
    }

    [Fact]
    public void MismatchingStateIsExternalRegardlessOfAge()
    {
        var pushedAt = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var pushed = new AmbientHuePushedState(
            pushedAt,
            new HueProjectedState(
                On: true,
                Brightness: 42,
                Xy: (0.3127f, 0.3290f)));

        // A genuine external command sets a different colour — detected as
        // external even within the former window. The safety net the
        // state-authoritative rule must keep.
        var update = new HueResourceUpdate(
            V2ResourceId: "v2-light",
            ResourceType: "light",
            CreationTime: pushedAt.AddMilliseconds(500),
            On: true,
            Brightness: 42,
            Xy: (0.5009f, 0.4149f));

        var decision = AmbientHueEchoClassifier.Classify(
            update,
            pushed,
            pushedAt.AddMilliseconds(500));

        Assert.Equal(AmbientHueEventDecisionKind.External, decision.Kind);
    }

    [Fact]
    public void XyOnlyMismatchIsTreatedAsEcho()
    {
        var pushedAt = DateTimeOffset.Parse("2026-06-28T00:02:21Z");
        var pushed = new AmbientHuePushedState(
            pushedAt,
            new HueProjectedState(
                On: true,
                Brightness: 87,
                Xy: (0.2771f, 0.2381f)));

        var update = new HueResourceUpdate(
            V2ResourceId: "v2-light-5",
            ResourceType: "light",
            CreationTime: pushedAt.AddMilliseconds(13),
            On: null,
            Brightness: null,
            Xy: (0.2832f, 0.2471f));

        var decision = AmbientHueEchoClassifier.Classify(
            update,
            pushed,
            pushedAt.AddMilliseconds(13));

        Assert.Equal(AmbientHueEventDecisionKind.Echo, decision.Kind);
    }

    [Fact]
    public void StatePayloadWithoutLastPushIsIgnored()
    {
        var eventAt = DateTimeOffset.Parse("2026-06-23T11:50:55Z");
        var update = new HueResourceUpdate(
            V2ResourceId: "v2-light-4",
            ResourceType: "light",
            CreationTime: eventAt,
            On: false,
            Brightness: null,
            Xy: null);

        var decision = AmbientHueEchoClassifier.Classify(
            update,
            lastPushed: null,
            nowUtc: eventAt);

        Assert.Equal(AmbientHueEventDecisionKind.Ignore, decision.Kind);
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

    // Regression — false external-stop observed 2026-06-04 (v1_id=4,
    // age_ms=4046, xy-only event). On a static zone the per-light
    // delta-gate suspends pushes, so the echo slot goes stale ; the bridge
    // re-reports our own colour past the old 2 s window. That late echo must
    // NOT be read as an external command — the engine would otherwise stop
    // itself. Matching state is our standing intent whatever its age.
    [Fact]
    public void StaleMatchingEchoOnStaticZoneIsNotExternal()
    {
        var pushedAt = DateTimeOffset.Parse("2026-06-04T09:28:41Z");
        var pushed = new AmbientHuePushedState(
            pushedAt,
            new HueProjectedState(
                On: true,
                Brightness: 180,
                Xy: (0.3272f, 0.3394f)));

        // The real event carried only xy (on=null, bri=null), 4046 ms
        // after our last push to that light.
        var update = new HueResourceUpdate(
            V2ResourceId: "v2-light-4",
            ResourceType: "light",
            CreationTime: pushedAt.AddMilliseconds(4046),
            On: null,
            Brightness: null,
            Xy: (0.3272f, 0.3394f));

        var decision = AmbientHueEchoClassifier.Classify(
            update,
            pushed,
            pushedAt.AddMilliseconds(4046));

        Assert.Equal(AmbientHueEventDecisionKind.Echo, decision.Kind);
    }
}
