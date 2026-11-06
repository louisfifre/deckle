using Xunit;

namespace Deckle.Audio.Tests;

// The voice detector's hysteresis. Every RMS is placed relative to the
// detector's own EntryDbfs and ExitDbfs, half a decibel off, so the tests
// follow a re-tune of the entry margin instead of pinning it.
[Trait("Category", "unit")]
public sealed class VoiceDetectorTests
{
    private static readonly double Level = new CaptureSettings().VoiceLevelDbfs;

    private static float Rms(double dbfs) => (float)Math.Pow(10.0, dbfs / 20.0);

    private static double Between(VoiceDetector d) => (d.ExitDbfs + d.EntryDbfs) / 2;

    [Fact]
    public void TheUsersLevelIsTheExitAndTheEntryLiesAboveIt()
    {
        var detector = new VoiceDetector(Level);

        Assert.Equal(Level, detector.ExitDbfs);
        Assert.True(detector.EntryDbfs > detector.ExitDbfs);
    }

    [Fact]
    public void SilenceStaysSilentAtALevelBetweenExitAndEntry()
    {
        var detector = new VoiceDetector(Level);

        Assert.False(detector.Push(Rms(Between(detector))));
        Assert.False(detector.Push(Rms(detector.EntryDbfs - 0.5)));
        Assert.False(detector.IsVoiced);
    }

    [Fact]
    public void AFrameAtOrAboveTheEntryOpensAVoicedRun()
    {
        var atEntry = new VoiceDetector(Level);
        var aboveEntry = new VoiceDetector(Level);

        Assert.True(atEntry.Push(Rms(atEntry.EntryDbfs)));
        Assert.True(aboveEntry.Push(Rms(aboveEntry.EntryDbfs + 0.5)));
    }

    [Fact]
    public void OnceVoicedAFrameBetweenExitAndEntryStaysVoiced()
    {
        var detector = new VoiceDetector(Level);
        detector.Push(Rms(detector.EntryDbfs + 0.5));

        Assert.True(detector.Push(Rms(Between(detector))));
        Assert.True(detector.Push(Rms(detector.ExitDbfs + 0.5)));
    }

    [Fact]
    public void AFrameBelowTheExitClosesTheVoicedRun()
    {
        var detector = new VoiceDetector(Level);
        detector.Push(Rms(detector.EntryDbfs + 0.5));

        Assert.False(detector.Push(Rms(detector.ExitDbfs - 0.5)));
        // Closed, the band no longer holds it: entry is needed again.
        Assert.False(detector.Push(Rms(Between(detector))));
    }

    [Fact]
    public void ResetReturnsToSilence()
    {
        var detector = new VoiceDetector(Level);
        detector.Push(Rms(detector.EntryDbfs + 0.5));

        detector.Reset();

        Assert.False(detector.IsVoiced);
        Assert.False(detector.Push(Rms(Between(detector))));
    }
}
