using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Tests;

// Ambient runs through a Hue Entertainment area only (decided 2026-11-25):
// the factory builds the stream for the area behind the group, or refuses
// with the reason the Settings page shows. No case falls back to driving the
// group another way.
[Trait("Category", "unit")]
public sealed class HueLightOutputFactoryTests
{
    [Fact]
    public async Task CreateAsyncReturnsUnconnectedEntertainmentWhenAvailable()
    {
        var entertainment = new FakeOutput();
        var bridge = FakeBridge.WithEntertainment(entertainment);

        var output = await HueLightOutputFactory.CreateAsync(bridge, "7", CancellationToken.None);

        Assert.Same(entertainment, output);
        Assert.False(entertainment.IsConnected);
    }

    [Fact]
    public async Task CreateAsyncRefusesAnAreaWithNoChannels()
    {
        var bridge = FakeBridge.WithEntertainment(
            new FakeOutput(),
            areas:
            [
                new HueEntertainmentArea(
                    "00112233-4455-6677-8899-aabbccddeeff",
                    "Living Room",
                    [],
                    []),
            ]);

        var ex = await Assert.ThrowsAsync<HueEntertainmentUnavailableException>(
            () => HueLightOutputFactory.CreateAsync(bridge, "7", CancellationToken.None));

        Assert.Equal(HueEntertainmentUnavailableReason.NoEntertainmentArea, ex.Reason);
    }

    [Fact]
    public async Task CreateAsyncRefusesAGroupNoAreaDescribes()
    {
        var bridge = FakeBridge.WithEntertainment(
            new FakeOutput(),
            groups: [new HueGroup("7", "Kitchen", "Room", 1)],
            groupLights: [new HueLight("9", "Pendant", "Extended color light", true)]);

        var ex = await Assert.ThrowsAsync<HueEntertainmentUnavailableException>(
            () => HueLightOutputFactory.CreateAsync(bridge, "7", CancellationToken.None));

        Assert.Equal(HueEntertainmentUnavailableReason.NoEntertainmentArea, ex.Reason);
    }

    [Fact]
    public async Task CreateAsyncRefusesABridgeWithNoClientKey()
    {
        var bridge = FakeBridge.WithEntertainment(new FakeOutput(), clientKey: "");

        var ex = await Assert.ThrowsAsync<HueEntertainmentUnavailableException>(
            () => HueLightOutputFactory.CreateAsync(bridge, "7", CancellationToken.None));

        Assert.Equal(HueEntertainmentUnavailableReason.NoClientKey, ex.Reason);
    }

    [Fact]
    public async Task CreateAsyncLetsACatalogFailurePropagate()
    {
        var bridge = FakeBridge.WithEntertainment(
            new FakeOutput(),
            catalogException: new HttpRequestException(
                "not supported",
                inner: null,
                System.Net.HttpStatusCode.NotFound));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => HueLightOutputFactory.CreateAsync(bridge, "7", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveEntertainmentAreaAsyncMatchesTheAreaByLightsWhenNamesDiffer()
    {
        var bridge = FakeBridge.WithEntertainment(
            new FakeOutput(),
            groups: [new HueGroup("7", "Desk", "Entertainment", 1)]);

        var area = await HueLightOutputFactory.ResolveEntertainmentAreaAsync(bridge, "7", CancellationToken.None);

        Assert.NotNull(area);
        Assert.Equal("Living Room", area!.Name);
    }

    [Fact]
    public async Task ConnectAsyncConnectsTheOutput()
    {
        var entertainment = new FakeOutput();

        await HueLightOutputFactory.ConnectAsync(entertainment, CancellationToken.None);

        Assert.True(entertainment.IsConnected);
    }

    [Fact]
    public async Task ConnectAsyncDisposesTheOutputAndRethrowsWhenTheStreamDoesNotOpen()
    {
        var entertainment = new FakeOutput { ConnectException = new InvalidOperationException("dtls failed") };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => HueLightOutputFactory.ConnectAsync(entertainment, CancellationToken.None));

        Assert.True(entertainment.Disposed);
    }

    [Fact]
    public async Task ConnectAsyncDisposesTheOutputOnCancellation()
    {
        var entertainment = new FakeOutput { ConnectException = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => HueLightOutputFactory.ConnectAsync(entertainment, CancellationToken.None));

        Assert.True(entertainment.Disposed);
    }

    private sealed class FakeBridge : IHueOutputFactoryBridge
    {
        private readonly FakeOutput _entertainment;
        private readonly IReadOnlyList<HueEntertainmentArea> _areas;
        private readonly IReadOnlyList<HueGroup> _groups;
        private readonly IReadOnlyList<HueLight> _groupLights;
        private readonly Exception? _catalogException;

        private FakeBridge(
            FakeOutput entertainment,
            string clientKey,
            IReadOnlyList<HueEntertainmentArea> areas,
            IReadOnlyList<HueGroup> groups,
            IReadOnlyList<HueLight> groupLights,
            Exception? catalogException)
        {
            _entertainment = entertainment;
            Credentials = new HueCredentials("user", clientKey);
            _areas = areas;
            _groups = groups;
            _groupLights = groupLights;
            _catalogException = catalogException;
        }

        public HueCredentials? Credentials { get; }

        public static FakeBridge WithEntertainment(
            FakeOutput entertainment,
            string clientKey = "aabbcc",
            IReadOnlyList<HueEntertainmentArea>? areas = null,
            IReadOnlyList<HueGroup>? groups = null,
            IReadOnlyList<HueLight>? groupLights = null,
            Exception? catalogException = null)
            => new(
                entertainment,
                clientKey,
                areas ??
                [
                    new HueEntertainmentArea(
                        "00112233-4455-6677-8899-aabbccddeeff",
                        "Living Room",
                        [],
                        [new HueEntertainmentChannel(1, "42", "Hue", 0, 0, 0, [])]),
                ],
                groups ?? [new HueGroup("7", "Living Room", "Entertainment", 1)],
                groupLights ?? [new HueLight("42", "Hue", "Extended color light", true)],
                catalogException);

        public Task<IReadOnlyList<HueEntertainmentArea>> ListEntertainmentConfigurationsAsync(CancellationToken ct)
            => _catalogException is null
                ? Task.FromResult(_areas)
                : Task.FromException<IReadOnlyList<HueEntertainmentArea>>(_catalogException);

        public Task<IReadOnlyList<HueGroup>> ListGroupsAsync(CancellationToken ct)
            => Task.FromResult(_groups);

        public Task<IReadOnlyList<HueLight>> ListLightsInGroupAsync(string groupId, CancellationToken ct)
            => Task.FromResult(_groupLights);

        public ILightOutput CreateEntertainmentOutput(HueEntertainmentArea area, string clientKey)
            => _entertainment;
    }

    private sealed class FakeOutput : IMultiLightOutput
    {
        public Exception? ConnectException { get; init; }
        public bool IsConnected { get; private set; }
        public bool Disposed { get; private set; }

        public Task ConnectAsync(CancellationToken ct = default)
        {
            if (ConnectException is not null)
                return Task.FromException(ConnectException);

            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task SetColorAsync(LightColor color, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<LightDescriptor>> ListLightsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LightDescriptor>>([]);

        public Task SetLightColorsAsync(IReadOnlyDictionary<string, LightColor> colorsByLightId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task IdentifyLightAsync(string lightId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task StopIdentifyAsync(string lightId, CancellationToken ct = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
