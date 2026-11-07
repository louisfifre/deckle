using Deckle.Audio.Internal;
using Xunit;

namespace Deckle.Audio.Tests;

// The capture against the real portaudio.dll and the machine's real audio
// stack: the DLL loads, the struct mirrors match it, the list holds
// microphones and no loopback capture, and a take on the default microphone
// returns 16 kHz audio of the length it ran. Opt-in, because it depends on the
// machine's devices: set DECKLE_AUDIO_RUN_SYSTEM=1. The take is skipped when
// the machine has no microphone.
[Trait("Category", "system")]
public sealed class PortAudioCaptureSystemTests
{
    private static void RequireOptIn() => Assert.SkipUnless(
        string.Equals(Environment.GetEnvironmentVariable("DECKLE_AUDIO_RUN_SYSTEM"), "1", StringComparison.Ordinal),
        "Set DECKLE_AUDIO_RUN_SYSTEM=1 to run the PortAudio system tests.");

    [Fact]
    public void TheMicrophoneListHoldsCaptureEndpointsOnly()
    {
        RequireOptIn();

        IReadOnlyList<PortAudioInputDevice> inputs = PortAudioHost.Invoke(PortAudioHost.ListInputs);

        // A capture endpoint id starts with {0.0.1.; a render one, which a
        // loopback capture carries, with {0.0.0.
        Assert.All(inputs, input => Assert.StartsWith("{0.0.1.", input.EndpointId, StringComparison.Ordinal));
        Assert.True(inputs.Count(input => input.IsDefault) <= 1);
    }

    [Fact]
    public void AnUnknownMicrophoneIsReportedAsNotDetected()
    {
        RequireOptIn();

        ProbeResult probe = new MicrophoneCapture().Probe("{0.0.1.00000000}.{00000000-0000-0000-0000-000000000000}");

        Assert.False(probe.Ok);
        Assert.Equal(MicErrorKind.NotDetected, probe.Kind);
    }

    [Fact]
    public void ATakeOnTheDefaultMicrophoneReturnsSixteenKilohertzAudio()
    {
        RequireOptIn();
        bool hasMicrophone = PortAudioHost.Invoke(() => PortAudioHost.ListInputs().Any(input => input.IsDefault));
        Assert.SkipUnless(hasMicrophone, "This machine has no default microphone.");

        var capture = new MicrophoneCapture();
        CaptureSource? source = null;
        int frames = 0;
        capture.Opened += s => source = s;
        capture.Frame += _ => frames++;

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        CaptureResult result = capture.Record(new Host(), stop.Token);

        Assert.Equal(CaptureOutcome.Completed, result.Outcome);
        Assert.NotNull(source);
        // 1.5 s at 16 kHz, give or take the scheduling of the stop.
        Assert.InRange(result.Pcm.Length, 16_000, 32_000);
        Assert.Equal(result.Pcm.Length / 800, frames);
    }

    private sealed class Host : IAudioRecordingHost
    {
        public string AudioInputEndpointId => "";
        public int MaxRecordingDurationSeconds => 0;
        public bool MicrophoneTelemetryEnabled => false;
        public double VoiceLevelDbfs => -45.0;
        public string RecordingPurpose => "system_test";
    }
}
