using System;
using System.Collections.Generic;

namespace Deckle.Inference.Onnx;

// Pure port of Silero's get_speech_timestamps decision logic (snakers4/silero-vad
// v5, the default max_speech_duration_s = inf path). The reference function is two
// phases: phase 1 runs the ONNX model per window to get one speech probability
// each (that lives in SileroVad), phase 2 — this — turns the probability sequence
// into padded speech [start,end) sample ranges. Kept model-free so it is unit
// tested directly on synthetic probability sequences.
//
// Faithful to the reference:
//   • a span's end is the FIRST sub-release-threshold window (temp_end), not the
//     window where the silence is finally confirmed;
//   • the min-silence wait keeps a span open across the hysteresis dead-band
//     (release_threshold <= p < threshold) — only p >= threshold cancels a
//     pending end;
//   • spans shorter than min-speech are dropped at close;
//   • the padding pass splits a tight gap between two spans by halves rather than
//     letting the padded spans overlap.
internal static class SileroSpeechTimestamps
{
    public const int SampleRate = 16000;
    public const int WindowSamples = 512;   // the v5 16 kHz chunk size

    // probs[i] is the speech probability of the window starting at sample
    // i * WindowSamples. audioLengthSamples bounds the tail span and the padding.
    public static List<SpeechSegment> Compute(
        IReadOnlyList<float> probs, int audioLengthSamples, SileroVadOptions options)
    {
        float threshold = options.Threshold;
        float negThreshold = MathF.Max(threshold - 0.15f, 0.01f);
        // The reference compares against un-floored float sample counts; mirror it.
        double minSpeech  = SampleRate * options.MinSpeechDurationMs  / 1000.0;
        double minSilence = SampleRate * options.MinSilenceDurationMs / 1000.0;
        int    speechPad  = (int)(SampleRate * options.SpeechPadMs    / 1000.0);

        var speeches = new List<SpeechSegment>();
        bool triggered = false;
        int curStart = 0;
        double tempEnd = 0;   // 0 means "no pending end"

        for (int i = 0; i < probs.Count; i++)
        {
            float p = probs[i];
            int cur = WindowSamples * i;

            // Speech resumed before the silence matured — cancel the pending end.
            if (p >= threshold && tempEnd != 0)
                tempEnd = 0;

            // Start of a span. `continue` so the close logic is skipped here.
            if (p >= threshold && !triggered)
            {
                triggered = true;
                curStart = cur;
                continue;
            }

            // Trailing silence — maybe close the span.
            if (p < negThreshold && triggered)
            {
                if (tempEnd == 0) tempEnd = cur;            // end = first silent window
                if (cur - tempEnd < minSilence) continue;   // not enough silence yet
                int end = (int)tempEnd;
                if (end - curStart > minSpeech)             // inline min-speech filter
                    speeches.Add(new SpeechSegment(curStart, end));
                triggered = false;
                tempEnd = 0;
            }
            // Dead-band (negThreshold <= p < threshold) while triggered: do nothing,
            // keep tempEnd — an in-progress silence countdown survives it.
        }

        // Tail flush: close a still-open span at the end of the audio.
        if (triggered && audioLengthSamples - curStart > minSpeech)
            speeches.Add(new SpeechSegment(curStart, audioLengthSamples));

        ApplyPadding(speeches, audioLengthSamples, speechPad);
        return speeches;
    }

    // Mirrors the reference padding pass: pad the first span's start and the last
    // span's end; between two spans, pad both sides unless the gap is tighter than
    // 2*pad, in which case split the gap by halves so the padded spans meet but do
    // not overlap. Each iteration writes the next span's start, so the following
    // iteration does not re-pad it.
    private static void ApplyPadding(List<SpeechSegment> speeches, int audioLen, int pad)
    {
        for (int i = 0; i < speeches.Count; i++)
        {
            int start = speeches[i].StartSample;
            int end   = speeches[i].EndSample;

            if (i == 0)
                start = Math.Max(0, start - pad);

            if (i != speeches.Count - 1)
            {
                SpeechSegment next = speeches[i + 1];
                int gap = next.StartSample - end;
                if (gap < 2 * pad)
                {
                    end = end + gap / 2;
                    speeches[i + 1] = next with { StartSample = Math.Max(0, next.StartSample - gap / 2) };
                }
                else
                {
                    end = Math.Min(audioLen, end + pad);
                    speeches[i + 1] = next with { StartSample = Math.Max(0, next.StartSample - pad) };
                }
            }
            else
            {
                end = Math.Min(audioLen, end + pad);
            }

            speeches[i] = new SpeechSegment(start, end);
        }
    }
}
