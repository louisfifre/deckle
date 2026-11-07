using Deckle.Audio.Internal;
using Xunit;

namespace Deckle.Audio.Tests;

// Pins the signal path of a take: stereo at 48 kHz in, exactly the delivered
// length at 16 kHz out, 50 ms frames and RMS log entries, and one level pulse
// per packet.
public class CapturePipelineTests
{
    private const int Rate = 48000;

    // Left 0.5, right 0.1: the mono mean is 0.3.
    private static float[] StereoPacket(int frames)
    {
        var packet = new float[frames * 2];
        for (int f = 0; f < frames; f++)
        {
            packet[2 * f] = 0.5f;
            packet[2 * f + 1] = 0.1f;
        }
        return packet;
    }

    [Fact]
    [Trait("Category", "unit")]
    public void OneSecondOfStereo_YieldsSixteenThousandSamplesTwentyFramesAndTwentyRmsEntries()
    {
        var rmsLog = new List<float>();
        var frames = new List<CaptureFrame>();
        var levels = new List<CaptureLevel>();
        var pipeline = new CapturePipeline(
            new CaptureFormat(Rate, 2), retainAudio: true, rmsLog,
            audioLevel: null, frame: frames.Add, level: levels.Add, lowAudio: null, onLowAudio: null);

        for (int i = 0; i < 100; i++)
            pipeline.Accept(StereoPacket(480), 480);
        pipeline.Finish();

        Assert.Equal(16000, pipeline.SampleCount);
        Assert.Equal(16000, pipeline.ToArray().Length);
        Assert.Equal(20, frames.Count);
        Assert.Equal(20, rmsLog.Count);
        Assert.All(frames, f => Assert.Equal(800, f.Samples.Length));
        for (int w = 2; w < 18; w++)
            Assert.Equal(0.3f, rmsLog[w], precision: 3);
        Assert.Equal(rmsLog, frames.Select(f => f.Rms));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Accept_FiresTheLevelCallbackOncePerPacket()
    {
        var levels = new List<CaptureLevel>();
        var pipeline = new CapturePipeline(
            new CaptureFormat(Rate, 2), retainAudio: true, [],
            audioLevel: null, frame: null, level: levels.Add, lowAudio: null, onLowAudio: null);

        for (int i = 1; i <= 5; i++)
        {
            pipeline.Accept(StereoPacket(480), 480);
            Assert.Equal(i, levels.Count);
        }
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Accept_FiresTheAudioLevelCallbackOncePerWindow()
    {
        var audioLevels = new List<float>();
        var pipeline = new CapturePipeline(
            new CaptureFormat(Rate, 2), retainAudio: false, [],
            audioLevels.Add, frame: null, level: null, lowAudio: null, onLowAudio: null);

        for (int i = 0; i < 100; i++)
            pipeline.Accept(StereoPacket(480), 480);
        pipeline.Finish();

        Assert.Equal(20, audioLevels.Count);
        Assert.Empty(pipeline.ToArray());
        Assert.Equal(16000, pipeline.SampleCount);
    }
}
