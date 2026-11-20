using System;

namespace Deckle.Inference.Onnx;

// Float to IEEE half narrowing the Float16 inputs go through (outputs are widened by the runtime's own Float16). Kept as
// pure code on System.Half so it is testable without a model.
internal static class HalfConversion
{
    public static ushort[] ToBits(float[] values)
    {
        var bits = new ushort[values.Length];
        for (int i = 0; i < values.Length; i++)
            bits[i] = BitConverter.HalfToUInt16Bits((Half)values[i]);
        return bits;
    }

}


