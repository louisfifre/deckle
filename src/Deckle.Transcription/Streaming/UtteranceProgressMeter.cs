using System;
using Deckle.Audio;

namespace Deckle.Transcription;

// Reads the pause gauge off a stream of microphone levels, with the cutting
// decision of a dictation and without one: the same EnergySegmenter state
// machine, fed the same 50 ms levels, reports the same UtteranceProgress at
// every step, and the utterances it cuts go nowhere.
//
// A caller that shows the gauge without dictating (the Playground's live
// preview) needs this and not the engine, which would also load a model and
// type the result. EnergySegmenter stays internal; this is its one public face.
//
// A level is all the segmenter's decision reads: it compares each frame's RMS
// to the threshold and counts frames. So the meter hands it frames with no
// samples, and a loud stretch that never ends holds no audio in memory. That is
// also why it can take the AudioLevel event, whose value is the RMS the Frame
// event carries: the capture then converts no float buffer at all.
//
// Single-threaded like the segmenter: Push and Progress belong to the one
// thread that delivers the levels. It stays silent in the detailed log, since
// a gauge preview is not a take.
public sealed class UtteranceProgressMeter
{
    private readonly EnergySegmenter _segmenter;

    // Settings are read once, here, as a dictation reads them when it starts.
    public UtteranceProgressMeter(EnergySegmenterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _segmenter = new EnergySegmenter(settings, static _ => { }, logsDetail: false);
    }

    // Where a take stands before its first level: active, not pausing, nothing
    // completed. A dictation publishes this before it subscribes to the frames.
    public UtteranceProgress Progress => _segmenter.Progress;

    // One 50 ms level, linear RMS [0, 1]. Returns the progress after it.
    public UtteranceProgress Push(float rms)
    {
        _segmenter.Push(new CaptureFrame(ReadOnlyMemory<float>.Empty, rms));
        return _segmenter.Progress;
    }
}
