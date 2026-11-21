namespace Deckle.Transcription.Parakeet;

// Deckle's segmenter puts no ceiling on an utterance, but one encoder pass over very long audio
// costs memory that grows with its length (see MaxChunkSamples). This chunker bridges the two: audio up
// to the cap goes through the encoder in one pass, as onnx-asr does, and only longer audio is cut into
// pieces. A cut in the middle of a word costs accuracy, and the decoder starts again from zero at each
// one, so each cut is placed in the quietest 200 ms found in the last 10 s before the limit (lowest RMS
// = most likely a pause).
// Chunks neither overlap nor leave gaps: concatenated, they are exactly the input, so no text is
// lost or repeated at a seam. Pure code, no model and no I/O.

/// <summary>Splits long 16 kHz mono audio into chunks the Parakeet encoder can take.</summary>
internal static class LongAudioChunker
{
    public const int SampleRate = 16_000;

    // Measured 2026-11-21 on one encoder pass (FP32) over a synthetic signal, peak private memory of the
    // process with the model loaded (about 2.1 to 2.5 GB at rest): DirectML on the RX 7900 XT, 120 s ->
    // 5.0 GB (4.7 GB of dedicated video memory), 240 s -> 8.7 GB (8.3), 480 s -> 15.6 GB (15.0); CPU,
    // 120 s -> 4.3 GB, 240 s -> 5.5 GB, 480 s -> 13.1 GB. 120 s is the largest length that stays under
    // about 6 GB on both. Why a cap at all rather than 30 s: cutting alone moved the transcript away from
    // the onnx-asr reference on 48 of 57 dictations of 30 s or more, and a single pass did not.
    /// <summary>Longest audio the encoder takes in one pass, and so the longest chunk: 120 s.</summary>
    public const int MaxChunkSamples = 120 * SampleRate;

    /// <summary>How far back from the limit a quiet cut point is looked for, 10 s.</summary>
    public const int SearchSamples = 10 * SampleRate;

    /// <summary>Width of the region whose RMS is compared, 200 ms.</summary>
    public const int QuietRegionSamples = SampleRate / 5;

    // Candidate cut regions start every 20 ms: finer steps gain nothing audible and cost more.
    private const int StepSamples = SampleRate / 50;

    /// <summary>
    /// Returns the chunks covering <paramref name="samples"/> exactly once, in order.
    /// A buffer of at most MaxChunkSamples (120 s) comes back as a single chunk; an empty one as no chunk.
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
