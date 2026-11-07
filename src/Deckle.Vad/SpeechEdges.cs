using System;
using System.Collections.Generic;

namespace Deckle.Vad;

// The edge trim of a take: the samples from the first speech span's start to the
// last one's end, pauses in between kept as spoken. Pure, so it is tested
// without the model; the engine feeds it the spans Silero found.
//
// The floor is the point. Silero drops a span shorter than
// SileroVadOptions.MinSpeechDurationMs (250 ms by default), which suits a cut
// into utterances: a blip there is noise. At the edge of a take it is a word.
// "Oui" said before a long sentence, or "merci" said last, lasts less than
// 250 ms; with the shared floor the span is dropped, the edge moves to the next
// span, and the word is cut off the audio Whisper receives. The edge detection
// therefore runs with a floor of one capture frame (50 ms): any voiced extent
// the model finds at an edge is kept. A click that lasts that long can pull the
// edge out by its own length, which costs Whisper some silence, not a word.
public static class SpeechEdges
{
    // One capture frame. Silero judges 32 ms windows, so a span must reach two
    // of them (64 ms) to clear this floor.
    public const int MinSpeechMs = 50;

    // The options the edge detection uses: the caller's, with the span floor
    // lowered to MinSpeechMs. A floor already lower is kept.
    public static SileroVadOptions OptionsFor(SileroVadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options with { MinSpeechDurationMs = Math.Min(options.MinSpeechDurationMs, MinSpeechMs) };
    }

    // The slice from the first span's start to the last span's end; empty when
    // there is no span. The span bounds come from the padded spans and stay
    // inside the buffer.
    public static float[] Slice(float[] samples, IReadOnlyList<SpeechSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Count == 0) return Array.Empty<float>();
        return samples[segments[0].StartSample..segments[^1].EndSample];
    }
}
