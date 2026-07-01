using Deckle.Lighting;
using Deckle.Lighting.Ambient;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "regression")]
public class AmbientHueChangeAttributorTests
{
    [Fact]
    public void PendingMatchingStateIsEcho()
    {
        var pushedAt = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var state = new AmbientHueAttributionState(
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

        var decision = AmbientHueChangeAttributor.Classify(
            update,
            state,
            pushedAt.AddMilliseconds(1200));

        Assert.Equal(AmbientHueChangeDecisionKind.Echo, decision.Kind);
    }

    [Fact]
    public void MatchingStateBeyondPendingWindowIsStillEcho()
    {
        var pushedAt = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var state = new AmbientHueAttributionState(
            pushedAt,
            new HueProjectedState(
                On: true,
                Brightness: 42,
                Xy: (0.3127f, 0.3290f)));

        var update = new HueResourceUpdate(
            V2ResourceId: "v2-light",
            ResourceType: "light",
            CreationTime: pushedAt.Add(AmbientHueChangeAttributor.PendingEchoWindow).AddMilliseconds(1),
            On: true,
            Brightness: 42,
            Xy: (0.3127f, 0.3290f));

        var decision = AmbientHueChangeAttributor.Classify(
            update,
            state,
            pushedAt.Add(AmbientHueChangeAttributor.PendingEchoWindow).AddMilliseconds(1));

        Assert.Equal(AmbientHueChangeDecisionKind.Echo, decision.Kind);
    }

    [Fact]
    public void XyOnlyMismatchWithinPendingWindowIsEcho()
    {
        var pushedAt = DateTimeOffset.Parse("2026-06-28T00:02:21Z");
        var state = new AmbientHueAttributionState(
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

        var decision = AmbientHueChangeAttributor.Classify(
            update,
            state,
            pushedAt.AddMilliseconds(13));

        Assert.Equal(AmbientHueChangeDecisionKind.Echo, decision.Kind);
    }

    [Fact]
    public void XyOnlyMismatchAfterPendingWindowIsExternal()
    {
        var pushedAt = DateTimeOffset.Parse("2026-06-28T00:02:21Z");
        var state = new AmbientHueAttributionState(
            pushedAt,
            new HueProjectedState(
                On: true,
                Brightness: 87,
                Xy: (0.2771f, 0.2381f)));

        var eventAt = pushedAt.Add(AmbientHueChangeAttributor.PendingEchoWindow).AddMilliseconds(1);
        var update = new HueResourceUpdate(
            V2ResourceId: "v2-light-5",
            ResourceType: "light",
            CreationTime: eventAt,
            On: null,
            Brightness: null,
            Xy: (0.2832f, 0.2471f));

        var decision = AmbientHueChangeAttributor.Classify(
            update,
            state,
            eventAt);

        Assert.Equal(AmbientHueChangeDecisionKind.External, decision.Kind);
    }

    [Fact]
    public void StrongMismatchWithinPendingWindowIsExternal()
    {
        var pushedAt = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var state = new AmbientHueAttributionState(
            pushedAt,
            new HueProjectedState(
                On: true,
                Brightness: 42,
                Xy: (0.3127f, 0.3290f)));

        var update = new HueResourceUpdate(
            V2ResourceId: "v2-light",
            ResourceType: "light",
            CreationTime: pushedAt.AddMilliseconds(500),
            On: false,
            Brightness: null,
            Xy: null);

        var decision = AmbientHueChangeAttributor.Classify(
            update,
            state,
            pushedAt.AddMilliseconds(500));

        Assert.Equal(AmbientHueChangeDecisionKind.External, decision.Kind);
    }

    [Fact]
    public void StatePayloadWithoutBaselineIsIgnored()
    {
        var eventAt = DateTimeOffset.Parse("2026-06-23T11:50:55Z");
        var update = new HueResourceUpdate(
            V2ResourceId: "v2-light-4",
            ResourceType: "light",
            CreationTime: eventAt,
            On: false,
            Brightness: null,
            Xy: null);

        var decision = AmbientHueChangeAttributor.Classify(
            update,
            state: null,
            nowUtc: eventAt);

        Assert.Equal(AmbientHueChangeDecisionKind.Ignore, decision.Kind);
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

        var offState = new AmbientHueAttributionState(
            pushedAt,
            new HueProjectedState(On: false, Brightness: null, Xy: null));
        var onState = new AmbientHueAttributionState(
            pushedAt,
            new HueProjectedState(On: true, Brightness: 50, Xy: (0.3f, 0.3f)));

        var echo = AmbientHueChangeAttributor.Classify(
            update,
            offState,
            pushedAt.AddMilliseconds(500));
        var external = AmbientHueChangeAttributor.Classify(
            update,
            onState,
            pushedAt.AddMilliseconds(500));

        Assert.Equal(AmbientHueChangeDecisionKind.Echo, echo.Kind);
        Assert.Equal(AmbientHueChangeDecisionKind.External, external.Kind);
    }

    // Regression — false external-stop observed 2026-06-04 (v1_id=4,
    // age_ms=4046, xy-only event). Hue can report its own color settling
    // after the old 2 s window. That remains a pending Ambient echo under
    // the attribution model, not proof of an external Hue command.
    [Fact]
    public void DelayedXyOnlySettlingWithinPendingWindowIsNotExternal()
    {
        var pushedAt = DateTimeOffset.Parse("2026-06-04T09:28:41Z");
        var state = new AmbientHueAttributionState(
            pushedAt,
            new HueProjectedState(
                On: true,
                Brightness: 180,
                Xy: (0.3272f, 0.3394f)));

        var update = new HueResourceUpdate(
            V2ResourceId: "v2-light-4",
            ResourceType: "light",
            CreationTime: pushedAt.AddMilliseconds(4046),
            On: null,
            Brightness: null,
            Xy: (0.3272f, 0.3394f));

        var decision = AmbientHueChangeAttributor.Classify(
            update,
            state,
            pushedAt.AddMilliseconds(4046));

        Assert.Equal(AmbientHueChangeDecisionKind.Echo, decision.Kind);
    }
}
