using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "regression")]
public sealed class PhantomUtteranceFilterTests
{
    // 2026-10-21: a 0.55 s noise blip about 1.5 s before the Stop press was
    // flushed at Stop, boosted by +16 dB and decoded as 12 text tokens
    // (avg_p 0.601, min_p 0.079, no_speech 0.000). Nothing rejected it. At 21.8
    // tokens/s it is the token_rate route that catches it; the low_confidence
    // route has its own tests below.
    [Fact]
    public void ABriefBlipDecodedIntoManyDoubtfulTokensIsRejected()
    {
        var phantom = new TranscriptionSegment(
            "x", 0, 60, Confidence: 0.601f, NoSpeechProb: 0f,
            TextTokens: 12, MinTokenProbability: 0.079f);

        PhantomUtteranceFilter.Verdict verdict = PhantomUtteranceFilter.Evaluate([phantom], 0.55);

        Assert.True(verdict.Rejected);
        Assert.Equal("token_rate", verdict.Reason);
        Assert.Equal(12, verdict.TextTokens);
    }

    // Few tokens at a plausible rate, but the model doubts them: low average AND a
    // collapsed token. Both bounds are read from the filter, so a retune moves the
    // inputs with it.
    [Fact]
    public void AShortDecodeTheModelDoubtsIsRejectedAsLowConfidence()
    {
        var segment = new TranscriptionSegment(
            "x", 0, 80, Confidence: PhantomUtteranceFilter.DoubtfulAverageProbability - 0.1f, NoSpeechProb: 0f,
            TextTokens: 4, MinTokenProbability: PhantomUtteranceFilter.DoubtfulMinimumProbability / 2);

        PhantomUtteranceFilter.Verdict verdict = PhantomUtteranceFilter.Evaluate([segment], 0.8);

        Assert.True(verdict.Rejected);
        Assert.Equal("low_confidence", verdict.Reason);
        Assert.True(verdict.TokensPerSecond < PhantomUtteranceFilter.MaxPlausibleTokensPerSecond);
    }

    // The bounds are strict (< on both), and both must hold: at either edge the
    // decode is kept.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void LowConfidenceNeedsBothBoundsStrictlyCrossed(bool averageAtEdge, bool minimumAtEdge)
    {
        float average = averageAtEdge
            ? PhantomUtteranceFilter.DoubtfulAverageProbability
            : PhantomUtteranceFilter.DoubtfulAverageProbability - 0.1f;
        float minimum = minimumAtEdge
            ? PhantomUtteranceFilter.DoubtfulMinimumProbability
            : PhantomUtteranceFilter.DoubtfulMinimumProbability / 2;
        var segment = new TranscriptionSegment(
            "x", 0, 80, Confidence: average, NoSpeechProb: 0f,
            TextTokens: 4, MinTokenProbability: minimum);

        Assert.False(PhantomUtteranceFilter.Evaluate([segment], 0.8).Rejected);
    }

    // Several segments are judged as one decode: the average is weighted by tokens
    // (a plain mean of the two segments would reject this one) and the minimum is
    // the lowest of the segments that carry text.
    [Fact]
    public void SegmentsAreJudgedTogetherWithATokenWeightedAverage()
    {
        var clear = new TranscriptionSegment(
            "x", 0, 40, Confidence: 0.9f, NoSpeechProb: 0f, TextTokens: 6, MinTokenProbability: 0.8f);
        var doubtful = new TranscriptionSegment(
            "y", 40, 80, Confidence: 0.2f, NoSpeechProb: 0f, TextTokens: 1, MinTokenProbability: 0.05f);

        PhantomUtteranceFilter.Verdict verdict = PhantomUtteranceFilter.Evaluate([clear, doubtful], 0.9);

        Assert.False(verdict.Rejected);
        Assert.Equal(7, verdict.TextTokens);
        Assert.Equal((6 * 0.9 + 0.2) / 7, verdict.AverageProbability, precision: 3);
        Assert.Equal(0.05, verdict.MinimumProbability, precision: 3);
    }
    [Fact]
    public void AClearShortAnswerIsKept()
    {
        var oui = new TranscriptionSegment(
            "x", 0, 40, Confidence: 0.92f, NoSpeechProb: 0.01f,
            TextTokens: 2, MinTokenProbability: 0.85f);

        Assert.False(PhantomUtteranceFilter.Evaluate([oui], 0.45).Rejected);
    }

    // A single hesitant word is not a phantom: the average carries it.
    [Fact]
    public void ADoubtfulMinimumAloneDoesNotRejectAShortAnswer()
    {
        var segment = new TranscriptionSegment(
            "x", 0, 50, Confidence: 0.88f, NoSpeechProb: 0f,
            TextTokens: 3, MinTokenProbability: 0.05f);

        Assert.False(PhantomUtteranceFilter.Evaluate([segment], 0.6).Rejected);
    }

    [Fact]
    public void AnImplausibleTokenRateRejectsEvenAConfidentDecode()
    {
        double audio = 0.5;
        int tokens = (int)(PhantomUtteranceFilter.MaxPlausibleTokensPerSecond * audio) + 3;
        var segment = new TranscriptionSegment(
            "x", 0, 50, Confidence: 0.95f, NoSpeechProb: 0f,
            TextTokens: tokens, MinTokenProbability: 0.6f);

        PhantomUtteranceFilter.Verdict verdict = PhantomUtteranceFilter.Evaluate([segment], audio);

        Assert.True(verdict.Rejected);
        Assert.Equal("token_rate", verdict.Reason);
    }

    // The same doubtful numbers on a full-length utterance are a hard
    // sentence, not a blip.
    [Fact]
    public void ALongUtteranceIsNeverRejected()
    {
        var segment = new TranscriptionSegment(
            "x", 0, 300, Confidence: 0.5f, NoSpeechProb: 0f,
            TextTokens: 40, MinTokenProbability: 0.01f);

        Assert.False(PhantomUtteranceFilter.Evaluate(
            [segment], PhantomUtteranceFilter.MaxPhantomAudioSeconds + 0.5).Rejected);
    }

    [Fact]
    public void AResultWithoutTokenCountsIsKept()
    {
        var segment = new TranscriptionSegment("x", 0, 30, 0.1f, 0f);

        Assert.False(PhantomUtteranceFilter.Evaluate([segment], 0.3).Rejected);
    }
}
