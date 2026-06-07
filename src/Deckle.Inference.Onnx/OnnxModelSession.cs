using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Deckle.Inference.Onnx;

// A loaded ONNX model run on the CPU execution provider. Wraps the OnnxRuntime
// InferenceSession with the policy a small model wants — single-threaded,
// sequential, off the GPU that whisper holds — and runs inference in plain
// arrays so callers never touch Microsoft.ML.OnnxRuntime types. That keeps the
// dependency isolated in this module: a consumer references Deckle.Inference.Onnx,
// not the runtime package.
public sealed class OnnxModelSession : IDisposable
{
    private readonly SessionOptions _options;
    private readonly InferenceSession _session;

    public OnnxModelSession(string modelPath)
    {
        var options = new SessionOptions
        {
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
            // The ctor threw (malformed/corrupt model): this instance is never
            // returned and Dispose() never runs, so release the native options
            // handle here instead of leaking it, then let the failure propagate.
            options.Dispose();
            throw;
        }
        _options = options;
    }

    // Runs one inference pass. Inputs are named tensors given as plain arrays plus
    // shapes; outputs come back by position (graph output order), each as a float
    // array the caller reads. The session is reused across calls.
    public IReadOnlyList<float[]> Run(IReadOnlyList<OnnxTensorInput> inputs)
    {
        var values = new NamedOnnxValue[inputs.Count];
        for (int i = 0; i < inputs.Count; i++)
            values[i] = inputs[i].ToNamedValue();

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(values);

        var outputs = new float[results.Count][];
        int o = 0;
        foreach (DisposableNamedOnnxValue result in results)
            outputs[o++] = result.AsTensor<float>().ToArray();
        return outputs;
    }

    public void Dispose()
    {
        _session.Dispose();
        _options.Dispose();
    }
}

// One named input tensor for OnnxModelSession.Run, carried as a plain array plus
// shape so the OnnxRuntime tensor types stay inside this module. Float and Int64
// cover the element types the current models use; add a factory when one needs
// another.
public readonly struct OnnxTensorInput
{
    private readonly string _name;
    private readonly float[]? _float;
    private readonly long[]? _long;
    private readonly int[] _shape;

    private OnnxTensorInput(string name, float[]? floatData, long[]? longData, int[] shape)
    {
        _name = name;
        _float = floatData;
        _long = longData;
        _shape = shape;
    }

    public static OnnxTensorInput Float(string name, float[] data, int[] shape) => new(name, data, null, shape);
    public static OnnxTensorInput Int64(string name, long[] data, int[] shape) => new(name, null, data, shape);

    internal NamedOnnxValue ToNamedValue() =>
        _float is not null
            ? NamedOnnxValue.CreateFromTensor(_name, new DenseTensor<float>(_float, _shape))
            : NamedOnnxValue.CreateFromTensor(_name, new DenseTensor<long>(_long!, _shape));
}
