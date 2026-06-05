using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

// RepetitionDetector is internal sealed in Deckle.Transcription.Whisper; the
// test project reaches it through InternalsVisibleTo declared in that module's
// csproj. Pure and deterministic: feed a sequence of segment texts and assert
// when the detector asks whisper to abort and with which period. We test the
// behaviour — does it abort, on which segment, as which loop shape — never the
// internal counters, so the tests survive a refactor of the matching.
[Trait("Category", "unit")]
public class RepetitionDetectorTests
{
    // Feeds segments in order through a fresh detector. Returns the 1-based index
    // of the segment that first trips the guard and the period it reported, or
    // (-1, 0) if no segment in the sequence aborts.
    private static (int Index, int Period) FirstAbort(params string[] segments)
    {
        var detector = new RepetitionDetector();
        for (int i = 0; i < segments.Length; i++)
        {
            if (detector.ObserveAndShouldAbort(segments[i], out _, out int period))
                return (i + 1, period);
        }
        return (-1, 0);
    }

    [Fact]
    public void DistinctSegmentsNeverAbort()
    {
        Assert.Equal((-1, 0), FirstAbort("un", "deux", "trois", "quatre", "cinq"));
    }

    [Fact]
    public void ThreeIdenticalSegmentsAbortAsPeriodOne()
    {
        Assert.Equal((3, 1), FirstAbort("le chat", "le chat", "le chat"));
    }

    [Fact]
    public void TwoIdenticalSegmentsDoNotAbort()
    {
        Assert.Equal((-1, 0), FirstAbort("le chat", "le chat"));
    }

    [Fact]
    public void StrictAlternationAbortsOnFirstFullRepetitionAsPeriodTwo()
    {
        // A B A B — the pair (A, B) completes its first strict repetition at the
        // fourth segment, so the guard trips there.
        Assert.Equal((4, 2), FirstAbort("alpha", "beta", "alpha", "beta"));
    }

    [Fact]
    public void SingleAlternationStepDoesNotAbort()
    {
        // A B A — only A has come back, the pair has not repeated yet.
        Assert.Equal((-1, 0), FirstAbort("alpha", "beta", "alpha"));
    }

    [Fact]
    public void MatchingIsCharacterExact_CaseDifferenceIsNotARepetition()
    {
        // Case-insensitive matching would read these as A A A and abort at 3.
        // The guard is strict, so a casing difference keeps them distinct.
        Assert.Equal((-1, 0), FirstAbort("Oui", "oui", "Oui"));
    }

    [Fact]
    public void EdgeWhitespaceIsTrimmedBeforeMatching()
    {
        // Whisper often prefixes a leading space; trimming makes these identical.
        Assert.Equal((3, 1), FirstAbort("merci", " merci", "merci  "));
    }

    [Fact]
    public void EmptySegmentsAreIgnoredAndDoNotBreakAStreak()
    {
        // Near-silence emits blank segments; they must be transparent, so the
        // three real segments still abort as period-1.
        Assert.Equal((5, 1), FirstAbort("stop", "", "stop", "   ", "stop"));
    }
}
