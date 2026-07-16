using System;
using System.Collections.Generic;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

// BackendAudio reconstruction in the normalized corpus contract: when the opt-in
// DSP runs, the per-utterance processed buffers are concatenated into the exact
// signal the backend received. TranscriptionEngine.ConcatSamples is the pure core
// of that reconstruction — these pin the ordering and length invariants the corpus
// record depends on, without standing up the threaded pipeline around it.
[Trait("Category", "unit")]
public class StreamingBackendAudioTests
{
    [Fact]
    public void ConcatPreservesOrderAndTotalLength()
    {
        var chunks = new List<float[]>
        {
            new[] { 1f, 2f },
            new[] { 3f, 4f, 5f },
            new[] { 6f },
        };

        float[] flat = TranscriptionEngine.ConcatSamples(chunks);

        Assert.Equal(new[] { 1f, 2f, 3f, 4f, 5f, 6f }, flat);
    }

    [Fact]
    public void ConcatOfEmptyListIsEmpty()
    {
        float[] flat = TranscriptionEngine.ConcatSamples(new List<float[]>());

        Assert.Empty(flat);
    }

    // An empty utterance buffer contributes no samples but must not desync the rest:
    // the kept utterances stay contiguous, in order, with no inserted gap.
    [Fact]
    public void ConcatKeepsEmptyChunksWithoutGaps()
    {
        var chunks = new List<float[]>
        {
            new[] { 1f },
            Array.Empty<float>(),
            new[] { 2f, 3f },
        };

        float[] flat = TranscriptionEngine.ConcatSamples(chunks);

        Assert.Equal(new[] { 1f, 2f, 3f }, flat);
    }
}
