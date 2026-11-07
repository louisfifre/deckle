using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Tests;

// 2026-11-07, the REST half of "stopping Ambient left the room almost dark":
// without an Entertainment key, with no matching area, or after a stream that
// failed, Ambient drives the group over REST and used to restore nothing, so
// the lights stayed on the last colour it pushed (black after a dark scene).
// Each test plays a bridge with lights on before Ambient, and checks what
// they end on after the output lets go.
[Trait("Category", "regression")]
public sealed class HueRestRestoreRegressionTests
{
    private static readonly HueLightState WarmWhite =
        new(true, 200, "ct", null, 366, null, null);
    private static readonly HueLightState Teal =
        new(true, 120, "xy", [0.17, 0.35], null, null, null);

    [Fact]
    public async Task StoppingReturnsTheGroupsLightsToTheirStateFromBeforeAmbient()
    {
        var bridge = new FakeBridge(new() { ["42"] = WarmWhite, ["43"] = Teal });
        var output = new HueRestLightOutput(bridge, "7", restoreStates: true);

        await output.ConnectAsync(TestContext.Current.CancellationToken);
        await output.SetLightColorsAsync(
            new Dictionary<string, LightColor> { ["42"] = LightColor.Black, ["43"] = LightColor.Black },
            TestContext.Current.CancellationToken);
        Assert.False(bridge.States["42"].On);
        await output.DisposeAsync();

        Assert.Equal(WarmWhite, bridge.States["42"]);
        Assert.Equal(Teal, bridge.States["43"]);
    }

    [Fact]
    public async Task ALightOutsideTheGroupIsNeverWritten()
    {
        var bridge = new FakeBridge(new() { ["42"] = WarmWhite, ["43"] = Teal, ["99"] = Teal });
        var output = new HueRestLightOutput(bridge, "7", restoreStates: true);

        await output.ConnectAsync(TestContext.Current.CancellationToken);
        await output.DisposeAsync();

        Assert.DoesNotContain("99", bridge.WrittenLightIds);
    }

    [Fact]
    public async Task AnOutputBuiltWithoutRestoreWritesNothingOnDispose()
    {
        // The Settings light list and light placement use the public
        // constructor: a restore there would write to lights nobody asked
        // to change.
        var bridge = new FakeBridge(new() { ["42"] = WarmWhite, ["43"] = Teal });
        var output = new HueRestLightOutput(bridge, "7", restoreStates: false);

        await output.ConnectAsync(TestContext.Current.CancellationToken);
        await output.DisposeAsync();

        Assert.Empty(bridge.WrittenLightIds);
        Assert.Equal(0, bridge.StateReads);
    }

    [Fact]
    public async Task WithoutAClientKeyTheFactorysRestOutputRestoresOnStop()
    {
        var bridge = new FakeBridge(new() { ["42"] = WarmWhite, ["43"] = Teal });
        var factory = new FakeFactoryBridge(bridge, clientKey: "");

        var output = await HueLightOutputFactory.CreatePreferredAsync(factory, "7", CancellationToken.None);
        output = await HueLightOutputFactory.ConnectPreparedPreferredAsync(
            factory, output, "7", allowRestFallback: false, CancellationToken.None);
        await ((IMultiLightOutput)output).SetLightColorsAsync(
            new Dictionary<string, LightColor> { ["42"] = LightColor.Black, ["43"] = LightColor.Black },
            TestContext.Current.CancellationToken);
        await output.DisposeAsync();

        Assert.IsType<HueRestLightOutput>(output);
        Assert.Equal(WarmWhite, bridge.States["42"]);
        Assert.Equal(Teal, bridge.States["43"]);
    }

    [Fact]
    public async Task AFallbackAfterAFailedStreamStartKeepsTheSnapshotFromBeforeAnyLightWasTouched()
    {
        // The stream's pre-prime dims the lights, the DTLS connect fails, and
        // the stream's own restore fails for light 42. A REST output that read
        // the lights afterwards would take the dimmed state as "before".
        var bridge = new FakeBridge(new() { ["42"] = WarmWhite, ["43"] = Teal })
        {
            FailWritesToLight = "42",
            FailWritesToLightCount = 1,
        };
        var factory = new FakeFactoryBridge(bridge, clientKey: "aabbcc", failTransport: true);

        var output = await HueLightOutputFactory.CreateConnectedPreferredAsync(
            factory, "7", CancellationToken.None);
        Assert.IsType<HueRestLightOutput>(output);
        Assert.NotEqual(WarmWhite, bridge.States["42"]);

        await output.DisposeAsync();

        Assert.Equal(WarmWhite, bridge.States["42"]);
        Assert.Equal(Teal, bridge.States["43"]);
    }

    private sealed class FakeFactoryBridge : IHueOutputFactoryBridge
    {
        private readonly FakeBridge _bridge;
        private readonly bool _failTransport;

        public FakeFactoryBridge(FakeBridge bridge, string clientKey, bool failTransport = false)
        {
            _bridge = bridge;
            _failTransport = failTransport;
            Credentials = new HueCredentials("user", clientKey);
        }

        public HueCredentials? Credentials { get; }

        public Task<IReadOnlyList<HueEntertainmentArea>> ListEntertainmentConfigurationsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<HueEntertainmentArea>>(
            [
                new HueEntertainmentArea(
                    "00112233-4455-6677-8899-aabbccddeeff",
                    "Living Room",
                    [],
                    [
                        new HueEntertainmentChannel(1, "42", "Left", 0, 0, 0, []),
                        new HueEntertainmentChannel(2, "43", "Right", 0, 0, 0, []),
                    ]),
            ]);

        public Task<IReadOnlyList<HueGroup>> ListGroupsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<HueGroup>>([new HueGroup("7", "Living Room", "Room", 2)]);

        public Task<IReadOnlyList<HueLight>> ListLightsInGroupAsync(string groupId, CancellationToken ct)
            => _bridge.ListLightsInGroupAsync(groupId, ct);

        public ILightOutput CreateEntertainmentOutput(HueEntertainmentArea area, string clientKey)
            => new HueEntertainmentLightOutput(
                _bridge,
                area,
                new FailingTransport(_failTransport),
                restoreWatchWindow: TimeSpan.Zero,
                restorePollInterval: TimeSpan.FromMilliseconds(10));

        public ILightOutput CreateRestOutput(string groupId)
            => new HueRestLightOutput(_bridge, groupId, restoreStates: true);
    }

    private sealed class FailingTransport(bool fail) : IHueEntertainmentTransport
    {
        public Task ConnectAsync(CancellationToken ct)
            => fail ? Task.FromException(new InvalidOperationException("dtls failed")) : Task.CompletedTask;

        public void Send(byte[] payload) { }
        public void Dispose() { }
    }

    // One state per light, the way the bridge keeps it. Group "7" holds
    // lights 42 and 43; any other light is outside it.
    private sealed class FakeBridge(Dictionary<string, HueLightState> initial) : IHueRestBridgeClient
    {
        public Dictionary<string, HueLightState> States { get; } = new(initial, StringComparer.Ordinal);
        public List<string> WrittenLightIds { get; } = [];
        public int StateReads { get; private set; }
        public string? FailWritesToLight { get; init; }
        public int FailWritesToLightCount { get; init; }
        private int _failuresLeft;
        private bool _failuresArmed;
        public bool IsPaired => true;

        public Task SetEntertainmentStreamingAsync(string id, bool active, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetGroupColorAsync(string groupId, LightColor color, CancellationToken ct = default)
        {
            foreach (var id in new[] { "42", "43" }) Apply(id, color);
            return Task.CompletedTask;
        }

        public Task SetLightColorAsync(string lightId, LightColor color, CancellationToken ct = default)
        {
            Apply(lightId, color);
            return Task.CompletedTask;
        }

        private void Apply(string lightId, LightColor color)
        {
            var (xy, bri) = HueColorMath.RgbToHueXyBri(color);
            States[lightId] = bri == 0
                ? States[lightId] with { On = false }
                : new HueLightState(true, bri, "xy", [xy.X, xy.Y], null, null, null);
        }

        public Task<IReadOnlyList<HueLight>> ListLightsInGroupAsync(string groupId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HueLight>>(
            [
                new HueLight("42", "Left", "Extended color light", true),
                new HueLight("43", "Right", "Extended color light", true),
            ]);

        public Task<IReadOnlyDictionary<string, HueLightState>> GetLightStatesAsync(CancellationToken ct = default)
        {
            StateReads++;
            return Task.FromResult<IReadOnlyDictionary<string, HueLightState>>(
                new Dictionary<string, HueLightState>(States, StringComparer.Ordinal));
        }

        public Task SetLightStateAsync(string lightId, HueLightState state, CancellationToken ct = default)
        {
            if (!_failuresArmed)
            {
                _failuresArmed = true;
                _failuresLeft = FailWritesToLightCount;
            }
            WrittenLightIds.Add(lightId);
            if (lightId == FailWritesToLight && _failuresLeft > 0)
            {
                _failuresLeft--;
                return Task.FromException(new HttpRequestException("bridge refused the write"));
            }
            States[lightId] = state;
            return Task.CompletedTask;
        }

        public Task IdentifyLightAsync(string lightId, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopIdentifyAsync(string lightId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
