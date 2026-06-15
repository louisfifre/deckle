using System;
using System.Collections.Generic;
using Deckle.Inference.Onnx;

namespace Deckle.Vad;

// Silero VAD over ONNX Runtime (CPU). Wraps a long-lived OnnxModelSession and the
// recurrent state Silero threads window-to-window. Loaded once and reused across
// utterances; Reset() (called at the start of every detection) clears the
// recurrent state so each buffer is independent.
//
// Runs at 16 kHz over 512-sample windows, each fed with a 64-sample context prefix
// carried from the previous window (the reference OnnxWrapper behaviour).
//
// Inference (this class) and the decision logic (SileroSpeechTimestamps) are kept
// separate on purpose: the state machine is pure and unit-tested, this is the thin
// model-bound part that cannot be exercised without the model file.
public sealed class SileroVad : IDisposable
{
    private const int SampleRate     = SileroSpeechTimestamps.SampleRate;     // 16000
    private const int WindowSamples  = SileroSpeechTimestamps.WindowSamples;  // 512
    private const int ContextSamples = 64;
    private const int StateLength    = 2 * 1 * 128;

    private readonly OnnxModelSession _session;

    // Recurrent state (zeros = initial) and the rolling context prefix, both
    // reused across windows. The 'sr' input never changes, so build it once.
    private readonly float[] _state   = new float[StateLength];
    private readonly float[] _context = new float[ContextSamples];
    private readonly float[] _input   = new float[ContextSamples + WindowSamples];
    private readonly long[]  _sr      = new long[] { SampleRate };

    // Per-window plumbing hoisted to fields so RunWindow allocates nothing. The
    // input tensors wrap the reused _input/_state/_sr buffers (shapes are
    // constant); the two destinations receive the outputs by position — _prob1
    // the probability, _state the new recurrent state copied in place for the
    // next window.
    private readonly float[] _prob1 = new float[1];
    private readonly OnnxTensorInput[] _inputs;
    private readonly float[][] _outputs;

    public SileroVad(string modelPath)
    {
        _session = new OnnxModelSession(modelPath);
        _inputs = new[]
        {
            OnnxTensorInput.Float("input", _input, new[] { 1, _input.Length }),
            OnnxTensorInput.Float("state", _state, new[] { 2, 1, 128 }),
            OnnxTensorInput.Int64("sr", _sr, new[] { 1 }),
        };
        _outputs = new[] { _prob1, _state };
    }

    // Detects the speech spans within a 16 kHz mono buffer, in sample indices
    // (padded per options). Resets the recurrent state first, so each call is
    // independent. An empty list means "no speech in this buffer".
    public IReadOnlyList<SpeechSegment> DetectSpeech(ReadOnlySpan<float> samples, SileroVadOptions options)
    {
        Reset();
        int windows = (samples.Length + WindowSamples - 1) / WindowSamples;
        var probs = new float[windows];
        for (int w = 0; w < windows; w++)
        {
            int offset = w * WindowSamples;
            int len = Math.Min(WindowSamples, samples.Length - offset);

            // Build [context | window]; right-zero-pad a short final window.
            _context.AsSpan().CopyTo(_input);
            samples.Slice(offset, len).CopyTo(_input.AsSpan(ContextSamples));
            if (len < WindowSamples)
                _input.AsSpan(ContextSamples + len).Clear();

            probs[w] = RunWindow();

            // Next window's context = the last 64 samples of what we just fed.
            _input.AsSpan(_input.Length - ContextSamples).CopyTo(_context);
        }
        return SileroSpeechTimestamps.Compute(probs, samples.Length, options);
    }

    // Trims a 16 kHz mono buffer to its speech, concatenating the detected spans.
    // Returns an empty buffer (SpeechSegments = 0) when no speech is found — the
    // caller drops the utterance rather than handing silence/noise to the ASR
    // backend. SpeechSegments carries the span count for observability.
    public SpeechTrimResult Trim(float[] samples, SileroVadOptions options)
    {
        IReadOnlyList<SpeechSegment> segments = DetectSpeech(samples, options);
        if (segments.Count == 0)
            return new SpeechTrimResult(Array.Empty<float>(), 0);

        int total = 0;
        for (int i = 0; i < segments.Count; i++) total += segments[i].LengthSamples;

        var trimmed = new float[total];
        int o = 0;
        foreach (SpeechSegment s in segments)
        {
            Array.Copy(samples, s.StartSample, trimmed, o, s.LengthSamples);
            o += s.LengthSamples;
        }
        return new SpeechTrimResult(trimmed, segments.Count);
    }

    private float RunWindow()
    {
        // Inputs and destinations are cached fields; the run writes the
        // probability into _prob1 and the new state into _state in place — by
        // position, the order the graph and the reference Python
        // (`out, state = ort_outs`) use, robust to the exact output node names —
        // so the next window picks up the state.
        _session.Run(_inputs, _outputs);
        return _prob1[0];
    }

    // Clears the recurrent state and context between independent buffers.
    public void Reset()
    {
        Array.Clear(_state);
        Array.Clear(_context);
    }

    public void Dispose() => _session.Dispose();
}
