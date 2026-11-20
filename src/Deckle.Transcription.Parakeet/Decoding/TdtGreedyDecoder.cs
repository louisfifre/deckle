namespace Deckle.Transcription.Parakeet.Decoding;

// Greedy decoding of a Token-and-Duration Transducer, a line-for-line port of the loop in
// onnx-asr v0.12.0 (asr.py _AsrWithTransducerDecoding._decoding, with nemo.py NemoConformerTdt._decode),
// so that our output can be compared with that reference. Each step the joint gives token logits and
// duration logits: the token is the argmax of the first, the jump the argmax of the second, and
// the duration classes are 0..N-1 frames, so the class index is the jump.

/// <summary>Greedy TDT decoder over the encoder frames of one utterance.</summary>
internal sealed class TdtGreedyDecoder
{
    /// <summary>onnx-asr's default for max_tokens_per_step.</summary>
    public const int DefaultMaxTokensPerStep = 10;

    private readonly ITdtJoint _joint;
    private readonly int _blankId;
    private readonly int _maxTokensPerStep;

    public TdtGreedyDecoder(ITdtJoint joint, int blankId, int maxTokensPerStep = DefaultMaxTokensPerStep)
    {
        ArgumentNullException.ThrowIfNull(joint);
        ArgumentOutOfRangeException.ThrowIfNegative(blankId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokensPerStep, 1);
        _joint = joint;
        _blankId = blankId;
        _maxTokensPerStep = maxTokensPerStep;
    }

    /// <summary>Decodes the frames in order and returns the emitted tokens with their frame and confidence.</summary>
    /// <exception cref="OperationCanceledException">Raised between two joint steps.</exception>
    public IReadOnlyList<TdtToken> Decode(IReadOnlyList<ReadOnlyMemory<float>> frames, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frames);

        var tokens = new List<TdtToken>();
        TdtDecoderState committed = _joint.CreateInitialState();
        int lastToken = _blankId;
        int t = 0;
        int emittedAtFrame = 0;

        while (t < frames.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TdtJointOutput output = _joint.Step(frames[t], lastToken, committed);
            int token = ArgMax(output.TokenLogits);
            int step = ArgMax(output.DurationLogits);

            if (token != _blankId)
            {
                // The state moves on only when a token is emitted: a blank leaves the prediction network where it was.
                committed = output.State;
                lastToken = token;
                tokens.Add(new TdtToken(token, t, SoftmaxProbability(output.TokenLogits, token)));
                emittedAtFrame++;
            }

            if (step > 0)
            {
                t += step;
                emittedAtFrame = 0;
            }
            else if (token == _blankId || emittedAtFrame == _maxTokensPerStep)
            {
                // Without the cap a zero jump on a non-blank token could loop on one frame forever.
                t++;
                emittedAtFrame = 0;
            }
        }

        return tokens;
    }

    /// <summary>First index of the maximum, like numpy's argmax.</summary>
    private static int ArgMax(float[] values)
    {
        if (values.Length == 0) throw new ArgumentException("A logits vector cannot be empty.", nameof(values));
        int best = 0;
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > values[best]) best = i;
        }
        return best;
    }

    /// <summary>exp(log_softmax)[index], computed against the maximum for numerical stability.</summary>
    private static float SoftmaxProbability(float[] logits, int index)
    {
        float max = logits[ArgMax(logits)];
        double sum = 0;
        foreach (float v in logits) sum += Math.Exp(v - max);
        return (float)(Math.Exp(logits[index] - max) / sum);
    }
}
