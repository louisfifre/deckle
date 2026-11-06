using System;

namespace Deckle.Audio;

// ── MicFrameLevels ─────────────────────────────────────────────────────────
//
// The per-frame reading of a mic-check capture, set against the voice level.
// The voice level applies to each 50 ms frame (the segmenter and the HUD test
// one frame at a time), so the check reads the capture frame by frame too: an
// average over the whole capture counts the pauses between words and reads a
// voice at -35 per frame as -50, well below a level it clears.
//
// LoudestTenthDbfs is the 90th percentile of the frames' levels, the level the
// loudest tenth of the capture reaches: high enough to sit on the words, not
// on the pauses, and below the very peak so one click cannot make the reading.
// VoicedShare is the share of frames a VoiceDetector at the voice level calls
// voiced, fed the same per-frame RMS the capture loop gives it.
//
// Pure, no state: Assess calls it, and the tests reach it directly.
internal static class MicFrameLevels
{
    // 50 ms at 16 kHz: the capture loop's frame, the one the voice level is
    // tested on.
    public const int FrameSamples = 800;

    // The percentile LoudestTenthDbfs reads.
    public const double Percentile = 0.9;

    // A frame of exact zeros: the DSP's floor for the same case.
    private const double SilenceFloorDbfs = -120.0;

    // Measures full frames only; a buffer shorter than one frame is read as one
    // frame. An empty buffer reads as silence with no voiced frame.
    public static MicFrameLevelReading Measure(ReadOnlySpan<float> pcm, double voiceLevelDbfs)
    {
        if (pcm.IsEmpty)
            return new MicFrameLevelReading(SilenceFloorDbfs, 0.0, 0);

        int frames = Math.Max(1, pcm.Length / FrameSamples);
        var levels = new double[frames];
        var detector = new VoiceDetector(voiceLevelDbfs);
        int voiced = 0;
        for (int f = 0; f < frames; f++)
        {
            ReadOnlySpan<float> frame = frames == 1 && pcm.Length < FrameSamples
                ? pcm
                : pcm.Slice(f * FrameSamples, FrameSamples);
            float rms = Rms(frame);
            levels[f] = rms <= 0f ? SilenceFloorDbfs : 20.0 * Math.Log10(rms);
            if (detector.Push(rms)) voiced++;
        }

        Array.Sort(levels);
        // Nearest rank: the smallest level at or above which a tenth of the
        // frames sit.
        int rank = (int)Math.Ceiling(Percentile * frames) - 1;
        double loudestTenth = levels[Math.Clamp(rank, 0, frames - 1)];

        return new MicFrameLevelReading(loudestTenth, (double)voiced / frames, frames);
    }

    private static float Rms(ReadOnlySpan<float> frame)
    {
        double sum = 0.0;
        foreach (float s in frame) sum += (double)s * s;
        return (float)Math.Sqrt(sum / frame.Length);
    }
}

// LoudestTenthDbfs: the 90th percentile of the frames' levels, in dBFS.
// VoicedShare: 0..1, the share of frames voiced at the voice level.
internal readonly record struct MicFrameLevelReading(
    double LoudestTenthDbfs,
    double VoicedShare,
    int Frames);
