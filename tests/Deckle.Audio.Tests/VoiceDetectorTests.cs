using Xunit;

namespace Deckle.Audio.Tests;

// The voice detector's two levels. The band tests build a detector with an
// explicit positive margin, since the shipped default is 0 (no band); every
// RMS is placed relative to the detector's own EntryDbfs and ExitDbfs, half a
// decibel off, so the tests follow a re-tune of that margin instead of
// pinning it.
[Trait("Category", "unit")]
public sealed class VoiceDetectorTests
{
    private static readonly double Level = new CaptureSettings().VoiceLevelDbfs;

    // Any positive margin wider than the half-decibel offsets below.
    private const double Margin = 3.0;

    private static float Rms(double dbfs) => (float)Math.Pow(10.0, dbfs / 20.0);

    private static double Between(VoiceDetector d) => (d.ExitDbfs + d.EntryDbfs) / 2;

    private static VoiceDetector WithBand() => new(Level, Margin);

    [Fact]
    public void WithTheDefaultMarginTheEntryIsTheLevelItself()
    {
        var detector = new VoiceDetector(Level);

        Assert.Equal(Level, detector.ExitDbfs);
        Assert.Equal(detector.ExitDbfs, detector.EntryDbfs);
    }

    [Fact]
    public void TheUsersLevelIsTheExitAndTheEntryLiesTheMarginAboveIt()
    {
        var detector = WithBand();

        Assert.Equal(Level, detector.ExitDbfs);
        Assert.True(detector.EntryDbfs > detector.ExitDbfs);
    }

    [Fact]
    public void SilenceStaysSilentAtALevelBetweenExitAndEntry()
    {
        var detector = WithBand();

        Assert.False(detector.Push(Rms(Between(detector))));
        Assert.False(detector.Push(Rms(detector.EntryDbfs - 0.5)));
        Assert.False(detector.IsVoiced);
    }

    [Fact]
    public void AFrameAtOrAboveTheEntryOpensAVoicedRun()
    {
        var atEntry = WithBand();
        var aboveEntry = WithBand();

        Assert.True(atEntry.Push(Rms(atEntry.EntryDbfs)));
        Assert.True(aboveEntry.Push(Rms(aboveEntry.EntryDbfs + 0.5)));
    }

    [Fact]
    public void OnceVoicedAFrameBetweenExitAndEntryStaysVoiced()
    {
        var detector = WithBand();
        detector.Push(Rms(detector.EntryDbfs + 0.5));

        Assert.True(detector.Push(Rms(Between(detector))));
        Assert.True(detector.Push(Rms(detector.ExitDbfs + 0.5)));
    }

    [Fact]
    public void AFrameBelowTheExitClosesTheVoicedRun()
    {
        var detector = WithBand();
        detector.Push(Rms(detector.EntryDbfs + 0.5));

        Assert.False(detector.Push(Rms(detector.ExitDbfs - 0.5)));
        // Closed, Push asks for the entry again.
        Assert.False(detector.Push(Rms(Between(detector))));
    }

    [Fact]
    public void ResumeAcceptsAFrameInTheBandAfterTheRunClosed()
    {
        var detector = WithBand();
        detector.Push(Rms(detector.EntryDbfs + 0.5));
        detector.Push(Rms(detector.ExitDbfs - 0.5));

        Assert.True(detector.Resume(Rms(Between(detector))));
        Assert.True(detector.IsVoiced);
    }

    [Fact]
    public void ResumeRefusesAFrameBelowTheExit()
    {
        var detector = WithBand();
        detector.Push(Rms(detector.EntryDbfs + 0.5));
        detector.Push(Rms(detector.ExitDbfs - 0.5));

        Assert.False(detector.Resume(Rms(detector.ExitDbfs - 0.5)));
        Assert.False(detector.IsVoiced);
    }

    [Fact]
    public void ResetReturnsToSilence()
    {
        var detector = WithBand();
        detector.Push(Rms(detector.EntryDbfs + 0.5));

        detector.Reset();

        Assert.False(detector.IsVoiced);
        Assert.False(detector.Push(Rms(Between(detector))));
    }
}
