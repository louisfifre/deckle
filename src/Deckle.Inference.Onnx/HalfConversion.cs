using System;

namespace Deckle.Inference.Onnx;

// Float to IEEE half conversion the Float16 inputs and outputs go through. Kept as
// pure code on System.Half so it is testable without a model and so the runtime's
// own Float16 type only appears at the call to the runtime.
internal static class HalfConversion
{
    public static ushort[] ToBits(float[] values)
    {
        var bits = new ushort[values.Length];
        for (int i = 0; i < values.Length; i++)
            bits[i] = BitConverter.HalfToUInt16Bits((Half)values[i]);
        return bits;
    }

    public static float ToFloat(ushort bits) => (float)BitConverter.UInt16BitsToHalf(bits);
}
