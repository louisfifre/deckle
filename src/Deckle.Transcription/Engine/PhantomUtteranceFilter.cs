namespace Deckle.Transcription;

// ── PhantomUtteranceFilter ───────────────────────────────────────────────────
//
// Rejects the decoded result of a short noise blip that Whisper turned into
// words. The energy segmenter and the VAD both let such a blip through (it is
// loud enough and long enough to be an utterance), the preprocessor then lifts
// it to the target level, and Whisper, handed 0.5 s of loud non-speech,
// decodes a dozen low-confidence tokens. Nothing downstream asks whether the
// decode is plausible for the audio it came from; this does.
//
// The decision reads the decoded result and the duration of the audio the
// backend received, so it holds for any utterance whatever cut it.
internal static class PhantomUtteranceFilter
{
    // Only audio shorter than this can be rejected. A phantom is a blip; a
    // longer span that decodes badly is a hard sentence, and dropping it would
    // lose real speech. A clearly spoken one-word answer ("oui", "ok") falls
    // under 1 s too; it is kept by the two tests below, not by its duration.
    internal const double MaxPhantomAudioSeconds = 1.0;

    // Text tokens per second of audio above which no speaker produced the
    // tokens. Fast French runs near 4 words/s, about 6 tokens/s; the observed
    // phantom was 12 tokens in 0.55 s, 21.8/s. 12/s is twice the fast-speech
    // rate, so a real short answer (1 to 3 tokens in 0.3 to 0.9 s, 1 to 10/s)
    // stays clear of it.
    internal const double MaxPlausibleTokensPerSecond = 12.0;

    // The second route: a short decode the model itself doubts. Both bounds
    // must hold, since a clear word can carry one uncertain token (low
    // minimum) with a high average, and a soft word can have a modest average
    // with no collapsed token. The observed phantom was avg 0.601, min 0.079;
    // real utterances measured 0.85 to 0.94 on average.
    internal const float DoubtfulAverageProbability = 0.7f;
    internal const float DoubtfulMinimumProbability = 0.1f;

    internal readonly record struct Verdict(
        bool Rejected,
        string Reason,
        int TextTokens,
        double TokensPerSecond,
        double AverageProbability,
        double MinimumProbability);

    // audioSeconds is the duration of the samples handed to the backend, after
    // any trim. A result that does not report text tokens (a backend that
    // leaves TextTokens at 0) cannot be judged and is kept.
    internal static Verdict Evaluate(IReadOnlyList<TranscriptionSegment> segments, double audioSeconds)
    {
        int tokens = 0;
        double weightedP = 0;
        float minP = 1f;
        for (int i = 0; i < segments.Count; i++)
        {
            TranscriptionSegment s = segments[i];
            tokens += s.TextTokens;
            weightedP += (double)s.Confidence * s.TextTokens;
            if (s.TextTokens > 0 && s.MinTokenProbability < minP) minP = s.MinTokenProbability;
        }

        if (tokens == 0 || audioSeconds <= 0)
            return new Verdict(false, "", tokens, 0, 0, 0);

        double avgP = weightedP / tokens;
        double rate = tokens / audioSeconds;

        if (audioSeconds >= MaxPhantomAudioSeconds)
            return new Verdict(false, "", tokens, rate, avgP, minP);

        if (rate > MaxPlausibleTokensPerSecond)
            return new Verdict(true, "token_rate", tokens, rate, avgP, minP);
        if (avgP < DoubtfulAverageProbability && minP < DoubtfulMinimumProbability)
            return new Verdict(true, "low_confidence", tokens, rate, avgP, minP);

        return new Verdict(false, "", tokens, rate, avgP, minP);
    }
}
