using System;
using System.Collections.Generic;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

// ConcatSamples rebuilds the corpus BackendAudio: the per-utterance processed
// buffers, concatenated into the exact signal the backend received.
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
