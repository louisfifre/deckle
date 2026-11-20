using System.Runtime.InteropServices;
using Deckle.Inference.Onnx;
using Deckle.Transcription.Parakeet.Decoding;

namespace Deckle.Transcription.Parakeet;

// The decoder_joint network behind ITdtJoint. One call is one step of the transducer:
// inputs encoder_outputs f32 [1,1024,1], targets i32 [1,1], target_length i32 [1] (INT32, not
// int64: the runtime refuses the wrong one), input_states_1 and input_states_2 f32 [2,1,640];
// the output "outputs" holds the token logits followed by the duration logits in one vector
// (8193 + 5), and output_states_1/2 are the next states. The other outputs the graph lists
// (lse, top-k, ...) are never requested, so the runtime does not compute them for us.

/// <summary>The decoder-joint network on an ONNX session, run on the CPU one frame at a time.</summary>
internal sealed class OnnxTdtJoint : ITdtJoint
{
    private const string StateName1 = "input_states_1";
    private const string StateName2 = "input_states_2";
    private static readonly string[] OutputNames = ["outputs", "output_states_1", "output_states_2"];

    private readonly OnnxModelSession _session;
    private readonly int _vocabularySize;
    private readonly int[] _stateShape1;
    private readonly int[] _stateShape2;

    /// <param name="session">The decoder_joint session.</param>
    /// <param name="vocabularySize">Count of the vocabulary, blank included: where the token logits end.</param>
    public OnnxTdtJoint(OnnxModelSession session, int vocabularySize)
    {
        _session = session;
        _vocabularySize = vocabularySize;
        _stateShape1 = StateShape(session, StateName1);
        _stateShape2 = StateShape(session, StateName2);
    }

    /// <summary>
    /// Checks that the graph is the one this class drives: both states exist and the joint output is
    /// wider than the vocabulary, so there is at least one duration logit after the tokens.
    /// </summary>
    /// <exception cref="InvalidDataException">The graph does not look like a Parakeet decoder-joint.</exception>
    public static void Validate(OnnxModelSession session, int vocabularySize)
    {
        OnnxTensorInfo? output = session.Outputs.FirstOrDefault(o => o.Name == OutputNames[0]);
        if (output is null || session.Inputs.All(i => i.Name != StateName1) || session.Inputs.All(i => i.Name != StateName2))
            throw new InvalidDataException("The decoder-joint graph lacks the expected inputs or outputs.");

        int last = output.Shape[^1];
        if (last != -1 && last <= vocabularySize)
            throw new InvalidDataException("The decoder-joint output is not wider than the vocabulary.");
    }

    public TdtDecoderState CreateInitialState() =>
        new(new float[Product(_stateShape1)], new float[Product(_stateShape2)]);

    public TdtJointOutput Step(ReadOnlyMemory<float> encoderFrame, int lastToken, TdtDecoderState state)
    {
        // Frames come from the encoder as whole arrays; copying only guards a caller that hands a slice.
        float[] frame = MemoryMarshal.TryGetArray(encoderFrame, out ArraySegment<float> segment)
                        && segment.Offset == 0 && segment.Count == segment.Array!.Length
            ? segment.Array
            : encoderFrame.ToArray();

        IReadOnlyList<OnnxTensorOutput> outputs = _session.RunNamed(
        [
            OnnxTensorInput.Float("encoder_outputs", frame, [1, frame.Length, 1]),
            OnnxTensorInput.Int32("targets", [lastToken], [1, 1]),
            OnnxTensorInput.Int32("target_length", [1], [1]),
            OnnxTensorInput.Float(StateName1, state.Hidden, _stateShape1),
            OnnxTensorInput.Float(StateName2, state.Cell, _stateShape2),
        ], OutputNames);

        float[] joint = outputs[0].Floats!;
        if (joint.Length <= _vocabularySize)
            throw new InvalidDataException("The decoder-joint output is not wider than the vocabulary.");

        // Token logits first, duration logits after, split at the vocabulary size (onnx-asr's
        // NemoConformerTdt._decode).
        float[] tokenLogits = joint[.._vocabularySize];
        float[] durationLogits = joint[_vocabularySize..];
        return new TdtJointOutput(tokenLogits, durationLogits, new TdtDecoderState(outputs[1].Floats!, outputs[2].Floats!));
    }

    // The state input is [layers, batch, hidden] with a dynamic batch; one utterance is batch 1.
    private static int[] StateShape(OnnxModelSession session, string name)
    {
        OnnxTensorInfo info = session.Inputs.First(i => i.Name == name);
        return info.Shape.Select(d => d < 0 ? 1 : d).ToArray();
    }

    private static int Product(int[] shape)
    {
        int product = 1;
        foreach (int d in shape) product *= d;
        return product;
    }
}
