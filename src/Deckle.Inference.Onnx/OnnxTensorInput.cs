using System;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Deckle.Inference.Onnx;

// One named input tensor for OnnxModelSession.Run, carried as a plain array plus
// shape so the OnnxRuntime tensor types stay inside this module. Float, Float16
// and Int64 cover the element types the current models use; add a factory when
// one needs another.
public readonly struct OnnxTensorInput
{
    private readonly string _name;
    private readonly float[]? _float;
    private readonly long[]? _long;
    private readonly ushort[]? _halfBits;
    private readonly int[] _shape;

    private OnnxTensorInput(string name, float[]? floatData, long[]? longData, ushort[]? halfBits, int[] shape)
    {
        _name = name;
        _float = floatData;
        _long = longData;
        _halfBits = halfBits;
        _shape = shape;
    }

    public static OnnxTensorInput Float(string name, float[] data, int[] shape) => new(name, data, null, null, shape);
    public static OnnxTensorInput Int64(string name, long[] data, int[] shape) => new(name, null, data, null, shape);

    // The caller holds float[]; the module narrows it to half here, once, so the
    // conversion is not repeated on every run that reuses the input.
    public static OnnxTensorInput Float16(string name, float[] data, int[] shape) =>
        new(name, null, null, HalfConversion.ToBits(data), shape);

    internal NamedOnnxValue ToNamedValue()
    {
        if (_float is not null)
            return NamedOnnxValue.CreateFromTensor(_name, new DenseTensor<float>(_float, _shape));
        if (_long is not null)
            return NamedOnnxValue.CreateFromTensor(_name, new DenseTensor<long>(_long, _shape));

        ushort[] bits = _halfBits!;
        var halves = new Float16[bits.Length];
        for (int i = 0; i < bits.Length; i++)
            halves[i] = new Float16(bits[i]);
        return NamedOnnxValue.CreateFromTensor(_name, new DenseTensor<Float16>(halves, _shape));
    }
}
