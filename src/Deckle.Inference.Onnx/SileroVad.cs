using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Deckle.Inference.Onnx;

// Silero VAD v5 over ONNX Runtime (CPU). Wraps a long-lived InferenceSession and
// the recurrent state Silero threads window-to-window. Loaded once and reused
// across utterances; Reset() (called at the start of every detection) clears the
// recurrent state so each buffer is independent.
//
// The model is the unified v5 silero_vad.onnx (16 kHz / 8 kHz in one file), run
// at 16 kHz: 512-sample windows, each fed with a 64-sample context prefix carried
// from the previous window (the reference OnnxWrapper behaviour — feeding the bare
// 512 reportedly runs too but is expected to shift the probabilities off the
// reference thresholds; inherited from the reference, not measured here).
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

    private readonly SessionOptions _options;
    private readonly InferenceSession _session;

    // Recurrent state (zeros = initial) and the rolling context prefix, both
    // reused across windows. The 'sr' input never changes, so build it once.
    private readonly float[] _state   = new float[StateLength];
    private readonly float[] _context = new float[ContextSamples];
    private readonly float[] _input   = new float[ContextSamples + WindowSamples];
    private readonly DenseTensor<long> _srTensor = new(new long[] { SampleRate }, new[] { 1 });

    public SileroVad(string modelPath)
    {
        var options = new SessionOptions
        {
            // Tiny recurrent model — thread-pool overhead would dominate; run it
            // single-threaded and sequential.
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };
        try
        {
            _session = new InferenceSession(modelPath, options);
        }
        catch
        {
            // A malformed/corrupt model makes the InferenceSession ctor throw. The
            // instance is never returned, so Dispose() never runs — release the
            // native SessionOptions handle here instead of leaking it to the
            // finalizer, then let the failure propagate to EnsureVadReady.
            options.Dispose();
            throw;
        }
        _options = options;
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
        var inputTensor = new DenseTensor<float>(_input, new[] { 1, _input.Length });
        var stateTensor = new DenseTensor<float>(_state, new[] { 2, 1, 128 });
        var inputs = new[]
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("state", stateTensor),
            NamedOnnxValue.CreateFromTensor("sr", _srTensor),
        };

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(inputs);

        // Read by position (probability, new state) — the order the v5 graph and
        // the reference Python (`out, state = ort_outs`) use; robust to the exact
        // output node names. Copy the state OUT before the result collection is
        // disposed, then feed it back next window.
        float prob = results.ElementAt(0).AsTensor<float>()[0];
        results.ElementAt(1).AsTensor<float>().ToArray().CopyTo(_state, 0);
        return prob;
    }

    // Clears the recurrent state and context between independent buffers.
    public void Reset()
    {
        Array.Clear(_state);
        Array.Clear(_context);
    }

    public void Dispose()
    {
        _session.Dispose();
        _options.Dispose();
    }
}
