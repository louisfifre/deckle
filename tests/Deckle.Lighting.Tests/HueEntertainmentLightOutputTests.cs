using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Tests;

[Trait("Category", "unit")]
public sealed class HueEntertainmentLightOutputTests
{
    [Fact]
    public async Task ConnectAsyncStopsStreamingWithIndependentTokenWhenTransportFails()
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
        Assert.False(client.LastStopTokenWasCancelled);
    }

    [Fact]
    public async Task ConnectAsyncStopsStreamingEvenWhenCallerTokenWasCancelled()
    {
        var client = new FakeEntertainmentClient();
        var transport = new FakeEntertainmentTransport
        {
            ConnectException = new OperationCanceledException(),
        };
        var output = new HueEntertainmentLightOutput(client, CreateArea(), transport);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => output.ConnectAsync(cts.Token));

        Assert.Equal(1, client.StartCount);
        Assert.Equal(1, client.StopCount);
        Assert.False(client.LastStopTokenWasCancelled);
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

        Assert.Single(transport.SentPayloads);
        Assert.Equal([1, 2, 3], ReadChannelIds(transport.SentPayloads[0]));
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

        Assert.Single(transport.SentPayloads);
        Assert.Equal([1, 2], ReadChannelIds(transport.SentPayloads[0]));
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

    private sealed class FakeEntertainmentClient : IHueEntertainmentBridgeClient
    {
        public bool IsPaired => true;
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
            }
            else
            {
                StopCount++;
                LastStopTokenWasCancelled = ct.IsCancellationRequested;
            }

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
        public List<byte[]> SentPayloads { get; } = [];

        public Task ConnectAsync(CancellationToken ct)
        {
            if (ConnectException is not null)
                return Task.FromException(ConnectException);

            return Task.CompletedTask;
        }

        public void Send(byte[] payload)
        {
            SentPayloads.Add(payload);
        }

        public void Dispose()
        {
        }
    }
}
