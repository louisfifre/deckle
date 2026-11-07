using Deckle.Audio.Internal;
using Xunit;

namespace Deckle.Audio.Tests;

// Pins the polling loop against a scripted device: the stop drains what is
// ready before the stream stops, the cap and a lost device end the take with
// the audio kept, a refused stop is reported, and the loop sleeps through the
// injected delegate when nothing is ready.
public class CaptureLoopTests
{
    private static PumpResult Pump(
        ScriptedCaptureEndpoint endpoint,
        FakeRecordingHost host,
        CancellationToken ct,
        Action<int>? sleep = null)
        => CaptureLoop.Pump(endpoint, host, [], null, null, null, null, "test", ct, sleep ?? (_ => { }));

    [Fact]
    [Trait("Category", "integration")]
    public void Stop_ReadsWhatIsReadyBeforeCallingStop()
    {
        var endpoint = new ScriptedCaptureEndpoint([]);
        using var cts = new CancellationTokenSource();
        int sleeps = 0;

        // The device delivers two packets while the loop sleeps, and the stop is requested.
        PumpResult result = Pump(endpoint, new FakeRecordingHost(), cts.Token, _ =>
        {
            if (++sleeps == 1)
            {
                endpoint.Enqueue(480, 480);
                cts.Cancel();
            }
        });

        Assert.Equal(LoopExit.StopRequested, result.Exit);
        Assert.Equal(["Read", "Read", "Stop"], endpoint.Calls);
        Assert.Equal(2, result.ReadsReceived);
        Assert.Equal(320, result.TotalSamples);
        Assert.Equal(320, result.Pcm.Length);
        Assert.Null(result.ReleaseStage);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void Stop_AfterKReads_StillReadsTheFramesLeftReadyThenStops()
    {
        using var cts = new CancellationTokenSource();
        var endpoint = new ScriptedCaptureEndpoint([480, 0, 480, 0, 480, 0, 480, 0]);
        endpoint.AfterRead = reads => { if (reads == 2) cts.Cancel(); };

        PumpResult result = Pump(endpoint, new FakeRecordingHost(), cts.Token);

        Assert.Equal(LoopExit.StopRequested, result.Exit);
        Assert.Equal("Stop", endpoint.Calls[^1]);
        Assert.Equal(1, endpoint.Calls.Count(c => c == "Stop"));
        Assert.DoesNotContain("Stop", endpoint.Calls.Take(endpoint.Calls.Count - 1));
        // The token fired inside read 2; read 3 was ready, and it comes before Stop.
        Assert.Equal(3, endpoint.ReadCount);
        Assert.Equal(480, result.TotalSamples);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void Cap_EndsTheTakeAsCapHitWithoutDrainingFurther()
    {
        // 100 ms packets, one per wake; 40 are scripted, the cap is 1 s.
        int[] script = Enumerable.Range(0, 40).SelectMany(_ => new[] { 4800, 0 }).ToArray();
        var endpoint = new ScriptedCaptureEndpoint(script);
        var host = new FakeRecordingHost { MaxRecordingDurationSeconds = 1 };

        PumpResult result = Pump(endpoint, host, CancellationToken.None);

        Assert.Equal(LoopExit.CapHit, result.Exit);
        Assert.InRange(endpoint.ReadCount, 10, 12);
        Assert.Equal(endpoint.ReadCount * 1600L, result.TotalSamples);
        Assert.Equal("Stop", endpoint.Calls[^1]);
        Assert.Null(result.ReleaseStage);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void NegativeAvailable_EndsAsDeviceLostKeepsAudioAndDoesNotCallStop()
    {
        int lost = CaptureErrors.AUDCLNT_E_DEVICE_INVALIDATED;
        var endpoint = new ScriptedCaptureEndpoint([4800, 0, 4800, 0, lost]);

        PumpResult result = Pump(endpoint, new FakeRecordingHost(), CancellationToken.None);

        Assert.Equal(LoopExit.DeviceLost, result.Exit);
        Assert.Equal(lost, result.LostCode);
        Assert.Equal(3200, result.TotalSamples);
        Assert.Equal(3200, result.Pcm.Length);
        Assert.DoesNotContain("Stop", endpoint.Calls);
        Assert.Null(result.ReleaseStage);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void NegativeAvailableMidDrain_EndsAsDeviceLostWithTheAudioReadBefore()
    {
        int lost = CaptureErrors.AUDCLNT_E_DEVICE_INVALIDATED;
        var endpoint = new ScriptedCaptureEndpoint([4800, lost]);

        PumpResult result = Pump(endpoint, new FakeRecordingHost(), CancellationToken.None);

        Assert.Equal(LoopExit.DeviceLost, result.Exit);
        Assert.Equal(lost, result.LostCode);
        Assert.Equal(1600, result.TotalSamples);
        Assert.DoesNotContain("Stop", endpoint.Calls);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void RefusedStop_IsReportedAsReleaseStageStop()
    {
        var endpoint = new ScriptedCaptureEndpoint([480, 0]) { StopCode = CaptureErrors.E_FAIL };
        using var cts = new CancellationTokenSource();
        endpoint.AfterRead = _ => cts.Cancel();

        PumpResult result = Pump(endpoint, new FakeRecordingHost(), cts.Token);

        Assert.Equal("stop", result.ReleaseStage);
        Assert.Equal(CaptureErrors.E_FAIL, result.ReleaseCode);
        Assert.Equal(LoopExit.StopRequested, result.Exit);
        Assert.Equal(160, result.TotalSamples);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void NothingReady_SleepsThroughTheInjectedDelegate()
    {
        var endpoint = new ScriptedCaptureEndpoint([]);
        using var cts = new CancellationTokenSource();
        var sleeps = new List<int>();

        PumpResult result = Pump(endpoint, new FakeRecordingHost(), cts.Token, ms =>
        {
            sleeps.Add(ms);
            if (sleeps.Count == 3) cts.Cancel();
        });

        Assert.Equal([CaptureLoop.PollIntervalMs, CaptureLoop.PollIntervalMs, CaptureLoop.PollIntervalMs], sleeps);
        Assert.Equal(0, result.TotalSamples);
        Assert.Equal(["Stop"], endpoint.Calls);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void HostThatRetainsNoAudio_ReturnsEmptyPcmWithTheRightTotal()
    {
        var endpoint = new ScriptedCaptureEndpoint([4800, 0, 4800, 0]);
        using var cts = new CancellationTokenSource();
        endpoint.AfterRead = reads => { if (reads == 2) cts.Cancel(); };

        PumpResult result = Pump(endpoint, new FakeRecordingHost { RetainsAudio = false }, cts.Token);

        Assert.Empty(result.Pcm);
        Assert.Equal(3200, result.TotalSamples);
    }
}

