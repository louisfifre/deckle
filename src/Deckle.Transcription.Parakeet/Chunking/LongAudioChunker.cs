namespace Deckle.Transcription.Parakeet;

// The Parakeet encoder handles about 30-35 s of audio per call, while Deckle's segmenter puts no
// ceiling on an utterance. This chunker bridges the two: it cuts a long buffer into pieces the
// encoder accepts. A cut in the middle of a word costs accuracy, so each cut is placed in the
// quietest 200 ms found in the last 10 s before the limit (lowest RMS = most likely a pause).
// Chunks neither overlap nor leave gaps: concatenated, they are exactly the input, so no text is
// lost or repeated at a seam. Pure code, no model and no I/O.

/// <summary>Splits long 16 kHz mono audio into chunks the Parakeet encoder can take.</summary>
internal static class LongAudioChunker
{
    public const int SampleRate = 16_000;

    /// <summary>Longest chunk, 30 s: inside the encoder's comfortable range with margin.</summary>
    public const int MaxChunkSamples = 30 * SampleRate;

    /// <summary>How far back from the limit a quiet cut point is looked for, 10 s.</summary>
    public const int SearchSamples = 10 * SampleRate;

    /// <summary>Width of the region whose RMS is compared, 200 ms.</summary>
    public const int QuietRegionSamples = SampleRate / 5;

    // Candidate cut regions start every 20 ms: finer steps gain nothing audible and cost more.
    private const int StepSamples = SampleRate / 50;

    /// <summary>
    /// Returns the chunks covering <paramref name="samples"/> exactly once, in order.
    /// A buffer of at most 30 s comes back as a single chunk; an empty one as no chunk.
    /// </summary>
    public static IReadOnlyList<AudioChunk> Split(ReadOnlySpan<float> samples)
    {
        var chunks = new List<AudioChunk>();
        int position = 0;
        while (samples.Length - position > MaxChunkSamples)
        {
            int cut = FindCut(samples, position);
            chunks.Add(new AudioChunk(position, cut - position));
            position = cut;
        }

        if (position < samples.Length)
            chunks.Add(new AudioChunk(position, samples.Length - position));
        return chunks;
    }

    // Cut point (absolute index) for a chunk that starts at chunkStart and must end by
    // chunkStart + MaxChunkSamples. It is the middle of the quietest region, so the cut sits
    // at the heart of the pause rather than at its edge.
    private static int FindCut(ReadOnlySpan<float> samples, int chunkStart)
    {
        int limit = chunkStart + MaxChunkSamples;
        int searchStart = limit - SearchSamples;

        // Running sum of squares over the search window, to get each region's energy in O(1).
        // double: summing 160k squared floats in float loses the small values that decide a tie.
        int windowLength = limit - searchStart;
        var prefix = new double[windowLength + 1];
        for (int i = 0; i < windowLength; i++)
        {
            double s = samples[searchStart + i];
            prefix[i + 1] = prefix[i] + s * s;
        }

        double bestEnergy = double.MaxValue;
        int bestStart = windowLength - QuietRegionSamples;
        for (int start = 0; start + QuietRegionSamples <= windowLength; start += StepSamples)
        {
            double energy = prefix[start + QuietRegionSamples] - prefix[start];
            // <= : on equal energy (digital silence) the later region wins, giving longer chunks.
            if (energy <= bestEnergy)
            {
                bestEnergy = energy;
                bestStart = start;
            }
        }

        return searchStart + bestStart + QuietRegionSamples / 2;
    }
}
