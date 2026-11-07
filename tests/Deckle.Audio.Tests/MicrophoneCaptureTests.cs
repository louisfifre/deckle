using Deckle.Audio.Internal;
using Xunit;

namespace Deckle.Audio.Tests;

// Pins what MicrophoneCapture does with an endpoint provider: a refused open
// becomes a MicError carrying the provider's code, Probe reads that code, and a
// successful take raises its events in order and none after it returns.
public class MicrophoneCaptureTests
{
    private static readonly CaptureDeviceIdentity Device = new("{0.0.1.00000000}.{test}", "Test microphone", IsDefault: true);

    private static FakeEndpointProvider Refusing(int code)
        => new((_, _) => CaptureOpenResult.Failed("open", code, Device, []));

    private static FakeEndpointProvider Serving(ScriptedCaptureEndpoint endpoint)
        => new((_, _) => CaptureOpenResult.Opened(endpoint, []));

    [Fact]
    [Trait("Category", "integration")]
    public void Record_WhenOpenFails_ReturnsMicErrorWithTheProvidersCode()
    {
        int code = CaptureErrors.AUDCLNT_E_DEVICE_IN_USE;
        using var capture = new MicrophoneCapture(Refusing(code), _ => { });

        CaptureResult result = capture.Record(new FakeRecordingHost(), CancellationToken.None);

        Assert.Equal(CaptureOutcome.MicError, result.Outcome);
        Assert.Equal(unchecked((uint)code), result.ErrorCode);
        Assert.Empty(result.Pcm);
    }

    [Theory]
    [InlineData(0x8889000Au, MicErrorKind.InUse)]
    [InlineData(0x80070490u, MicErrorKind.NotDetected)]
    [InlineData(0x80004005u, MicErrorKind.Unavailable)]
    [Trait("Category", "integration")]
    public void Probe_WhenOpenFails_ClassifiesTheCode(uint code, MicErrorKind expected)
    {
        using var capture = new MicrophoneCapture(Refusing(unchecked((int)code)), _ => { });

        ProbeResult result = capture.Probe("");

        Assert.False(result.Ok);
        Assert.Equal(expected, result.Kind);
        Assert.Equal(code, result.ErrorCode);
    }

    // portaudio.dll beside the wrong folder reached the user as a bare -10000,
    // the same code a device refusal can carry. The stage is what tells a
    // library that did not load from a device that did not answer.
    [Theory]
    [InlineData("load")]
    [InlineData("resolve")]
    [InlineData("open")]
    [Trait("Category", "regression")]
    public void Probe_WhenOpenFails_NamesTheStageThatRefused(string stage)
    {
        var provider = new FakeEndpointProvider(
            (_, _) => CaptureOpenResult.Failed(stage, PortAudioInterop.NotInitialized, Device, []));
        using var capture = new MicrophoneCapture(provider, _ => { });

        ProbeResult result = capture.Probe("");

        Assert.False(result.Ok);
        Assert.Equal(stage, result.Stage);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void Probe_WhenOpenSucceeds_ReleasesTheEndpointAndNeverStartsIt()
    {
        var endpoint = new ScriptedCaptureEndpoint([]);
        var provider = Serving(endpoint);
        using var capture = new MicrophoneCapture(provider, _ => { });

        ProbeResult result = capture.Probe("{id}");

        Assert.True(result.Ok);
        Assert.Equal(["Dispose"], endpoint.Calls);
        Assert.False(provider.Requests[0].Options.Exclusive);
        Assert.Equal("{id}", provider.Requests[0].EndpointId);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void Record_OnASuccessfulTake_RaisesOpenedThenStartedThenFramesAndNothingAfter()
    {
        var endpoint = new ScriptedCaptureEndpoint([48000, 0], new CaptureFormat(48000, 1));
        using var cts = new CancellationTokenSource();
        var events = new List<string>();
        using var capture = new MicrophoneCapture(Serving(endpoint), _ => cts.Cancel());
        capture.Opened += _ => events.Add("opened");
        capture.CaptureStarted += () => events.Add("started");
        capture.Frame += _ => events.Add("frame");

        CaptureResult result = capture.Record(new FakeRecordingHost(), cts.Token);
        int countAtReturn = events.Count;
        Thread.Sleep(50);

        Assert.Equal(CaptureOutcome.Completed, result.Outcome);
        Assert.Equal(16000, result.Pcm.Length);
        Assert.Equal(["opened", "started"], events.Take(2));
        Assert.Equal(20, events.Skip(2).Count(e => e == "frame"));
        Assert.Equal(22, countAtReturn);
        Assert.Equal(countAtReturn, events.Count);
        Assert.Equal(["Start", "Read", "Read", "Read", "Read", "Read", "Read", "Read", "Read", "Read", "Read", "Stop", "Dispose"], endpoint.Calls);
    }

    [Theory]
    [InlineData(CaptureMode.Raw, false)]
    [InlineData(CaptureMode.Default, true)]
    [Trait("Category", "integration")]
    public void Record_OpenedReportsTheModeAndExclusivityTheEndpointGave(CaptureMode mode, bool exclusive)
    {
        var endpoint = new ScriptedCaptureEndpoint([], new CaptureFormat(44100, 2), exclusive: exclusive, mode: mode);
        using var cts = new CancellationTokenSource();
        CaptureSource? source = null;
        using var capture = new MicrophoneCapture(Serving(endpoint), _ => cts.Cancel());
        capture.Opened += s => source = s;

        // The host asks for the opposite of what the endpoint gives: the report is the endpoint's.
        capture.Record(new FakeRecordingHost { ExclusiveMode = !exclusive }, cts.Token);

        Assert.NotNull(source);
        Assert.Equal(mode, source.Value.Mode);
        Assert.Equal(exclusive, source.Value.Exclusive);
        Assert.Equal(44100, source.Value.SampleRate);
        Assert.Equal(2, source.Value.Channels);
        Assert.Equal("Test microphone", source.Value.DeviceName);
    }
}
