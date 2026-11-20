namespace Deckle.Transcription.Parakeet.Decoding;

// One call of the decoder_joint network, seen from the decoding loop. The loop is written against
// this interface so it can be tested with a scripted fake and so that the ONNX session, its tensors
// and its execution provider stay out of the algorithm.

/// <summary>The two recurrent state arrays of the prediction network (LSTM hidden and cell).</summary>
/// <remarks>Opaque to the decoder: it only carries them from one step to the next.</remarks>
internal sealed record TdtDecoderState(float[] Hidden, float[] Cell);

/// <summary>Output of one decoder_joint step.</summary>
/// <param name="TokenLogits">One logit per vocabulary entry, blank included.</param>
/// <param name="DurationLogits">One logit per duration class; class i means a jump of i frames.</param>
/// <param name="State">Prediction-network state after consuming the input token.</param>
internal sealed record TdtJointOutput(float[] TokenLogits, float[] DurationLogits, TdtDecoderState State);

/// <summary>Runs the decoder_joint network one step at a time.</summary>
internal interface ITdtJoint
{
    /// <summary>State to start every utterance from: all zeros, shaped by the network's inputs.</summary>
    TdtDecoderState CreateInitialState();

    /// <summary>Joins one encoder frame with the prediction network fed by <paramref name="lastToken"/>.</summary>
    /// <param name="encoderFrame">The encoder output at one time index.</param>
    /// <param name="lastToken">Last emitted token, or the blank id when none was emitted yet.</param>
    /// <param name="state">State committed after the last non-blank token.</param>
    TdtJointOutput Step(ReadOnlyMemory<float> encoderFrame, int lastToken, TdtDecoderState state);
}
