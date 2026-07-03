using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Tests;

[Trait("Category", "unit")]
public sealed class HueEntertainmentLightOutputTests
{
    [Fact]
    public async Task ConnectAsyncStopsStreamingWhenTransportFails()
    {
        var client = new FakeEntertainmentClient();
        var transport = new FakeEntertainmentTransport
        {
            ConnectException = new InvalidOperationException("dtls failed"),
        };
        var output = new HueEntertainmentLightOutput(client, CreateArea(), transport);

        await Assert.ThrowsAsync<InvalidOperationException>(() => output.ConnectAsync(CancellationToken.None));

        Assert.Equal(1, client.StartCount);
        Assert.Equal(1, client.StopCount);
    }

    [Fact]
    public async Task ConnectAsyncDoesNotStartStreamingWhenCancelledBeforeStart()
    {
        var client = new FakeEntertainmentClient();
        var transport = new FakeEntertainmentTransport();
        var output = new HueEntertainmentLightOutput(client, CreateArea(), transport);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => output.ConnectAsync(cts.Token));

        Assert.Equal(0, client.StartCount);
        Assert.Equal(0, client.StopCount);
    }

    [Fact]
    public void EntertainmentOutputDoesNotUseStateEventAttribution()
    {
        var output = new HueEntertainmentLightOutput(
            new FakeEntertainmentClient(),
            CreateArea(),
            new FakeEntertainmentTransport());

        Assert.False(((ILightOutput)output).UsesStateEventAttribution);
    }

    [Fact]
    public void RestOutputUsesStateEventAttribution()
    {
        var client = new HueBridgeClient(
            new HueBridge("bridge", "192.168.1.2", 443),
            new HueCredentials("user", ""));
        var output = new HueRestLightOutput(client, "7");

        Assert.True(output.UsesStateEventAttribution);
    }

    [Fact]
    public async Task ConnectAsyncPrePrimesThenStartsStreamingBeforeConnectingTransportAndPrimingBlack()
    {
        var events = new List<string>();
        var client = new FakeEntertainmentClient { Events = events };
        var transport = new FakeEntertainmentTransport { Events = events };
        var output = new HueEntertainmentLightOutput(client, CreateArea(), transport);

        await output.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["preprime:42", "start", "connect", "send"], events);
    }

    [Fact]
    public async Task ConnectAsyncPrePrimesDistinctEntertainmentLightsWithDimOnState()
    {
        var client = new FakeEntertainmentClient();
        var output = new HueEntertainmentLightOutput(
            client,
            CreateArea(
                new HueEntertainmentChannel(1, "42", "Left A", 0, 0, 0, []),
                new HueEntertainmentChannel(2, "42", "Left B", 0, 0, 0, []),
                new HueEntertainmentChannel(3, "43", "Right", 0, 0, 0, []),
                new HueEntertainmentChannel(4, null, "Zone", 0, 0, 0, [])),
            new FakeEntertainmentTransport());

        await output.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["42", "43"], client.PrePrimedLightIds);
        Assert.All(client.PrePrimedColors, color => Assert.Equal(new LightColor(1, 1, 1), color));
    }

    [Fact]
    public async Task ConnectAsyncContinuesWhenPrePrimeFails()
    {
        var client = new FakeEntertainmentClient
        {
            SetLightColorException = new InvalidOperationException("rest failed"),
        };
        var output = new HueEntertainmentLightOutput(client, CreateArea(), new FakeEntertainmentTransport());

        await output.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, client.StartCount);
        Assert.True(output.IsConnected);
    }

    [Fact]
    public async Task ConnectAsyncPrimesEveryEntertainmentChannelWithBlack()
    {
        var transport = new FakeEntertainmentTransport();
        var output = new HueEntertainmentLightOutput(
            new FakeEntertainmentClient(),
            CreateArea(
                new HueEntertainmentChannel(1, "42", "Left", 0, 0, 0, []),
                new HueEntertainmentChannel(2, "43", "Right", 0, 0, 0, [])),
            transport);

        await output.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Single(transport.SentPayloads);
        Assert.Equal([1, 2], ReadChannelIds(transport.SentPayloads[0]));
        Assert.All(ReadChannelColors(transport.SentPayloads[0]), color => Assert.Equal((0, 0, 0), color));
    }

    [Fact]
    public async Task SetColorAsyncSendsEveryEntertainmentChannel()
    {
        var transport = new FakeEntertainmentTransport();
        var output = new HueEntertainmentLightOutput(
            new FakeEntertainmentClient(),
            CreateArea(
                new HueEntertainmentChannel(1, "42", "Left", 0, 0, 0, []),
                new HueEntertainmentChannel(2, "43", "Right", 0, 0, 0, []),
                new HueEntertainmentChannel(3, null, "Zone", 0, 0, 0, [])),
            transport);

        await output.ConnectAsync(TestContext.Current.CancellationToken);
        await output.SetColorAsync(new LightColor(10, 20, 30), TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.SentPayloads.Count);
        Assert.Equal([1, 2, 3], ReadChannelIds(transport.SentPayloads[^1]));
    }

    [Fact]
    public async Task SetLightColorsAsyncFansOutMappedLightChannelsAndIgnoresUnknownIds()
    {
        var transport = new FakeEntertainmentTransport();
        var output = new HueEntertainmentLightOutput(
            new FakeEntertainmentClient(),
            CreateArea(
                new HueEntertainmentChannel(1, "42", "Left A", 0, 0, 0, []),
                new HueEntertainmentChannel(2, "42", "Left B", 0, 0, 0, []),
                new HueEntertainmentChannel(3, "99", "Other", 0, 0, 0, [])),
            transport);

        await output.ConnectAsync(TestContext.Current.CancellationToken);
        await output.SetLightColorsAsync(
            new Dictionary<string, LightColor>
            {
                ["42"] = new(10, 20, 30),
                ["missing"] = new(40, 50, 60),
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.SentPayloads.Count);
        Assert.Equal([1, 2], ReadChannelIds(transport.SentPayloads[^1]));
    }

    private static HueEntertainmentArea CreateArea()
        => CreateArea(new HueEntertainmentChannel(1, "42", "Hue", 0, 0, 0, []));

    private static HueEntertainmentArea CreateArea(params HueEntertainmentChannel[] channels)
        => new(
            "00112233-4455-6677-8899-aabbccddeeff",
            "Living Room",
            [],
            channels);

    private static int[] ReadChannelIds(byte[] frame)
    {
        var ids = new List<int>();
        for (int offset = 52; offset < frame.Length; offset += 7)
            ids.Add(frame[offset]);
        return ids.ToArray();
    }

    private static (int R, int G, int B)[] ReadChannelColors(byte[] frame)
    {
        var colors = new List<(int R, int G, int B)>();
        for (int offset = 52; offset < frame.Length; offset += 7)
            colors.Add((frame[offset + 1], frame[offset + 3], frame[offset + 5]));
        return colors.ToArray();
    }

    private sealed class FakeEntertainmentClient : IHueEntertainmentBridgeClient
    {
        public bool IsPaired => true;
        public List<string>? Events { get; init; }
        public Exception? SetLightColorException { get; init; }
        public List<string> PrePrimedLightIds { get; } = [];
        public List<LightColor> PrePrimedColors { get; } = [];
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public bool LastStopTokenWasCancelled { get; private set; }

        public Task SetEntertainmentStreamingAsync(
            string entertainmentConfigurationId,
            bool active,
            CancellationToken ct = default)
        {
            if (active)
            {
                StartCount++;
                Events?.Add("start");
            }
            else
            {
                StopCount++;
                LastStopTokenWasCancelled = ct.IsCancellationRequested;
                Events?.Add("stop");
            }

            return Task.CompletedTask;
        }

        public Task SetLightColorAsync(string lightId, LightColor color, CancellationToken ct = default)
        {
            if (SetLightColorException is not null)
                return Task.FromException(SetLightColorException);

            PrePrimedLightIds.Add(lightId);
            PrePrimedColors.Add(color);
            Events?.Add($"preprime:{lightId}");
            return Task.CompletedTask;
        }

        public Task IdentifyLightAsync(string lightId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task StopIdentifyAsync(string lightId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeEntertainmentTransport : IHueEntertainmentTransport
    {
        public Exception? ConnectException { get; init; }
        public List<string>? Events { get; init; }
        public List<byte[]> SentPayloads { get; } = [];

        public Task ConnectAsync(CancellationToken ct)
        {
            if (ConnectException is not null)
                return Task.FromException(ConnectException);

            Events?.Add("connect");
            return Task.CompletedTask;
        }

        public void Send(byte[] payload)
        {
            Events?.Add("send");
            SentPayloads.Add(payload);
        }

        public void Dispose()
        {
        }
    }
}
