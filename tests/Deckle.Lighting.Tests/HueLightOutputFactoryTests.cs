using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Tests;

[Trait("Category", "unit")]
public sealed class HueLightOutputFactoryTests
{
    [Fact]
    public async Task CreatePreferredAsyncReturnsUnconnectedEntertainmentWhenAvailable()
    {
        var entertainment = new FakeOutput();
        var rest = new FakeOutput();
        var bridge = FakeBridge.WithEntertainment(entertainment, rest);

        var output = await HueLightOutputFactory.CreatePreferredAsync(bridge, "7", CancellationToken.None);

        Assert.Same(entertainment, output);
        Assert.False(entertainment.IsConnected);
        Assert.False(rest.IsConnected);
    }

    [Fact]
    public async Task CreatePreferredAsyncUsesRestWhenEntertainmentHasNoChannels()
    {
        var entertainment = new FakeOutput();
        var rest = new FakeOutput();
        var bridge = FakeBridge.WithEntertainment(
            entertainment,
            rest,
            areas:
            [
                new HueEntertainmentArea(
                    "00112233-4455-6677-8899-aabbccddeeff",
                    "Living Room",
                    [],
                    []),
            ]);

        var output = await HueLightOutputFactory.CreatePreferredAsync(bridge, "7", CancellationToken.None);

        Assert.Same(rest, output);
        Assert.False(entertainment.IsConnected);
        Assert.False(rest.IsConnected);
    }

    [Fact]
    public async Task CreatePreferredAsyncDoesNotFallbackWhenBridgeIsUnreachable()
    {
        var bridge = FakeBridge.WithEntertainment(
            new FakeOutput(),
            new FakeOutput(),
            catalogException: new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => HueLightOutputFactory.CreatePreferredAsync(bridge, "7", CancellationToken.None));
    }

    [Fact]
    public async Task CreatePreferredAsyncFallsBackWhenBridgeRejectsEntertainmentCatalog()
    {
        var rest = new FakeOutput();
        var bridge = FakeBridge.WithEntertainment(
            new FakeOutput(),
            rest,
            catalogException: new HttpRequestException(
                "not supported",
                inner: null,
                System.Net.HttpStatusCode.NotFound));

        var output = await HueLightOutputFactory.CreatePreferredAsync(
            bridge,
            "7",
            CancellationToken.None);

        Assert.Same(rest, output);
    }

    [Fact]
    public async Task CreateConnectedPreferredAsyncUsesConnectedEntertainmentWhenAvailable()
    {
        var entertainment = new FakeOutput();
        var rest = new FakeOutput();
        var bridge = FakeBridge.WithEntertainment(entertainment, rest);

        var output = await HueLightOutputFactory.CreateConnectedPreferredAsync(bridge, "7", CancellationToken.None);

        Assert.Same(entertainment, output);
        Assert.True(entertainment.IsConnected);
        Assert.False(rest.IsConnected);
    }

    [Fact]
    public async Task CreateConnectedPreferredAsyncFallsBackToRestWhenEntertainmentConnectFails()
    {
        var entertainment = new FakeOutput { ConnectException = new InvalidOperationException("dtls failed") };
        var rest = new FakeOutput();
        var bridge = FakeBridge.WithEntertainment(entertainment, rest);

        var output = await HueLightOutputFactory.CreateConnectedPreferredAsync(bridge, "7", CancellationToken.None);

        Assert.Same(rest, output);
        Assert.True(rest.IsConnected);
        Assert.True(entertainment.Disposed);
    }

    [Fact]
    public async Task CreateConnectedPreferredAsyncDoesNotFallBackOnCancellation()
    {
        var entertainment = new FakeOutput { ConnectException = new OperationCanceledException() };
        var rest = new FakeOutput();
        var bridge = FakeBridge.WithEntertainment(entertainment, rest);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => HueLightOutputFactory.CreateConnectedPreferredAsync(bridge, "7", CancellationToken.None));

        Assert.False(rest.IsConnected);
        Assert.True(entertainment.Disposed);
    }

    [Fact]
    public async Task CreateConnectedPreferredAsyncUsesRestWhenClientKeyIsMissing()
    {
        var entertainment = new FakeOutput();
        var rest = new FakeOutput();
        var bridge = FakeBridge.WithEntertainment(entertainment, rest, clientKey: "");

        var output = await HueLightOutputFactory.CreateConnectedPreferredAsync(bridge, "7", CancellationToken.None);

        Assert.Same(rest, output);
        Assert.False(entertainment.IsConnected);
        Assert.True(rest.IsConnected);
    }

    [Fact]
    public async Task ConnectPreparedPreferredAsyncFallsBackToRestWhenEntertainmentConnectFails()
    {
        var entertainment = new FakeOutput { ConnectException = new InvalidOperationException("dtls failed") };
        var rest = new FakeOutput();
        var bridge = FakeBridge.WithEntertainment(entertainment, rest);

        var output = await HueLightOutputFactory.ConnectPreparedPreferredAsync(
            bridge,
            entertainment,
            "7",
            allowRestFallback: true,
            CancellationToken.None);

        Assert.Same(rest, output);
        Assert.True(rest.IsConnected);
        Assert.True(entertainment.Disposed);
    }

    [Fact]
    public async Task ConnectPreparedPreferredAsyncDoesNotFallBackOnCancellation()
    {
        var entertainment = new FakeOutput { ConnectException = new OperationCanceledException() };
        var rest = new FakeOutput();
        var bridge = FakeBridge.WithEntertainment(entertainment, rest);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => HueLightOutputFactory.ConnectPreparedPreferredAsync(
                bridge,
                entertainment,
                "7",
                allowRestFallback: true,
                CancellationToken.None));

        Assert.False(rest.IsConnected);
        Assert.True(entertainment.Disposed);
    }

    private sealed class FakeBridge : IHueOutputFactoryBridge
    {
        private readonly FakeOutput _entertainment;
        private readonly FakeOutput _rest;
        private readonly IReadOnlyList<HueEntertainmentArea> _areas;
        private readonly IReadOnlyList<HueGroup> _groups;
        private readonly Exception? _catalogException;

        private FakeBridge(
            FakeOutput entertainment,
            FakeOutput rest,
            string clientKey,
            IReadOnlyList<HueEntertainmentArea> areas,
            IReadOnlyList<HueGroup> groups,
            Exception? catalogException)
        {
            _entertainment = entertainment;
            _rest = rest;
            Credentials = new HueCredentials("user", clientKey);
            _areas = areas;
            _groups = groups;
            _catalogException = catalogException;
        }

        public HueCredentials? Credentials { get; }

        public static FakeBridge WithEntertainment(
            FakeOutput entertainment,
            FakeOutput rest,
            string clientKey = "aabbcc",
            IReadOnlyList<HueEntertainmentArea>? areas = null,
            Exception? catalogException = null)
            => new(
                entertainment,
                rest,
                clientKey,
                areas ??
                [
                    new HueEntertainmentArea(
                        "00112233-4455-6677-8899-aabbccddeeff",
                        "Living Room",
                        [],
                        [new HueEntertainmentChannel(1, "42", "Hue", 0, 0, 0, [])]),
                ],
                [new HueGroup("7", "Living Room", "Room", 1)],
                catalogException);

        public Task<IReadOnlyList<HueEntertainmentArea>> ListEntertainmentConfigurationsAsync(CancellationToken ct)
            => _catalogException is null
                ? Task.FromResult(_areas)
                : Task.FromException<IReadOnlyList<HueEntertainmentArea>>(_catalogException);

        public Task<IReadOnlyList<HueGroup>> ListGroupsAsync(CancellationToken ct)
            => Task.FromResult(_groups);

        public Task<IReadOnlyList<HueLight>> ListLightsInGroupAsync(string groupId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<HueLight>>([new HueLight("42", "Hue", "Extended color light", true)]);

        public ILightOutput CreateEntertainmentOutput(HueEntertainmentArea area, string clientKey)
            => _entertainment;

        public ILightOutput CreateRestOutput(string groupId)
            => _rest;
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
