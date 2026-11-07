using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Tests;

// 2026-11-07: after stopping Ambient in a dark room the lights stayed almost
// black. The pre-prime dims every area light just before action=start, and
// the bridge's own post-stream return goes back to the state it saw at
// start, that near-black. Each test plays a bridge with lights on before
// streaming and checks what they end on after the output lets go.
[Trait("Category", "regression")]
public sealed class HueEntertainmentRestoreRegressionTests
{
    private static readonly HueLightState WarmWhite =
        new(true, 200, "ct", null, 366, null, null);
    private static readonly HueLightState Teal =
        new(true, 120, "xy", [0.17, 0.35], null, null, null);

    [Fact]
    public async Task StoppingReturnsTheLightsToTheirStateFromBeforeStreaming()
    {
        var bridge = new FakeBridge(new() { ["42"] = WarmWhite, ["43"] = Teal });
        var output = CreateOutput(bridge, watch: TimeSpan.Zero);

        await output.ConnectAsync(TestContext.Current.CancellationToken);
        await output.DisposeAsync();

        Assert.Equal(WarmWhite, bridge.States["42"]);
        Assert.Equal(Teal, bridge.States["43"]);
    }

    [Fact]
    public async Task StoppingOutlastsTheBridgesOwnReturnToThePrePrime()
    {
        // "Last on-state": a while after action=stop, the bridge writes back
        // what it saw at start, the pre-prime, over Deckle's first restore.
        var bridge = new FakeBridge(new() { ["42"] = WarmWhite, ["43"] = Teal })
        {
            RevertToStartStateAfterReads = 1,
        };
        var output = CreateOutput(bridge, watch: TimeSpan.FromMilliseconds(200));

        await output.ConnectAsync(TestContext.Current.CancellationToken);
        await output.DisposeAsync();

        Assert.True(bridge.Reverted);
        Assert.Equal(WarmWhite, bridge.States["42"]);
        Assert.Equal(Teal, bridge.States["43"]);
    }

    [Fact]
    public async Task AFailedStartReturnsThePrePrimedLights()
    {
        var bridge = new FakeBridge(new() { ["42"] = WarmWhite, ["43"] = Teal });
        var output = CreateOutput(
            bridge,
            watch: TimeSpan.Zero,
            transport: new FakeTransport { ConnectException = new InvalidOperationException("dtls failed") });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => output.ConnectAsync(TestContext.Current.CancellationToken));

        Assert.Equal(WarmWhite, bridge.States["42"]);
        Assert.Equal(Teal, bridge.States["43"]);
    }

    [Fact]
    public async Task ALightThatWasOffStaysOff()
    {
        var off = new HueLightState(false, 0, "xy", [0.3, 0.3], null, null, null);
        var bridge = new FakeBridge(new() { ["42"] = off, ["43"] = Teal })
        {
            RevertToStartStateAfterReads = 1,
        };
        var output = CreateOutput(bridge, watch: TimeSpan.FromMilliseconds(200));

        await output.ConnectAsync(TestContext.Current.CancellationToken);
        await output.DisposeAsync();

        Assert.False(bridge.States["42"].On);
    }

    private static HueEntertainmentLightOutput CreateOutput(
        FakeBridge bridge,
        TimeSpan watch,
        FakeTransport? transport = null)
        => new(
            bridge,
            new HueEntertainmentArea(
                "00112233-4455-6677-8899-aabbccddeeff",
                "Living Room",
                [],
                [
                    new HueEntertainmentChannel(1, "42", "Left", 0, 0, 0, []),
                    new HueEntertainmentChannel(2, "43", "Right", 0, 0, 0, []),
                ]),
            transport ?? new FakeTransport(),
            restoreWatchWindow: watch,
            restorePollInterval: TimeSpan.FromMilliseconds(10));

    // Keeps one state per light the way the bridge does: REST colour writes
    // (the pre-prime) and state writes replace it, and the stream's start
    // remembers it so a post-stream return can write it back.
    private sealed class FakeBridge(Dictionary<string, HueLightState> initial) : IHueEntertainmentBridgeClient
    {
        private Dictionary<string, HueLightState>? _atStreamStart;
        private int _readsAfterStop = -1;

        public Dictionary<string, HueLightState> States { get; } = new(initial, StringComparer.Ordinal);
        public int RevertToStartStateAfterReads { get; init; } = -1;
        public bool Reverted { get; private set; }
        public bool IsPaired => true;

        public Task SetEntertainmentStreamingAsync(string id, bool active, CancellationToken ct = default)
        {
            if (active) _atStreamStart = new Dictionary<string, HueLightState>(States, StringComparer.Ordinal);
            else _readsAfterStop = 0;
            return Task.CompletedTask;
        }

        public Task SetLightColorAsync(string lightId, LightColor color, CancellationToken ct = default)
        {
            var (xy, bri) = HueColorMath.RgbToHueXyBri(color);
            States[lightId] = bri == 0
                ? States[lightId] with { On = false }
                : new HueLightState(true, bri, "xy", [xy.X, xy.Y], null, null, null);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, HueLightState>> GetLightStatesAsync(CancellationToken ct = default)
        {
            if (_readsAfterStop >= 0 && ++_readsAfterStop == RevertToStartStateAfterReads && _atStreamStart is not null)
            {
                foreach (var (id, state) in _atStreamStart) States[id] = state;
                Reverted = true;
            }
            return Task.FromResult<IReadOnlyDictionary<string, HueLightState>>(
                new Dictionary<string, HueLightState>(States, StringComparer.Ordinal));
        }

        public Task SetLightStateAsync(string lightId, HueLightState state, CancellationToken ct = default)
        {
            States[lightId] = state;
            return Task.CompletedTask;
        }

        public Task IdentifyLightAsync(string lightId, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopIdentifyAsync(string lightId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeTransport : IHueEntertainmentTransport
    {
        public Exception? ConnectException { get; init; }

        public Task ConnectAsync(CancellationToken ct)
            => ConnectException is null ? Task.CompletedTask : Task.FromException(ConnectException);

        public void Send(byte[] payload) { }
        public void Dispose() { }
    }
}
