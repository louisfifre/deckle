using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Deckle.Inference.Onnx;

// A loaded ONNX model. Wraps the OnnxRuntime InferenceSession with the policy
// each execution target wants and runs inference in plain arrays, so callers
// never touch Microsoft.ML.OnnxRuntime types. That keeps the dependency isolated
// in this module: a consumer references Deckle.Inference.Onnx, not the runtime
// package. The default (no options) is the CPU policy a small model wants,
// single-threaded and sequential, off the GPU another engine may hold.
public sealed class OnnxModelSession : IDisposable
{
    private readonly SessionOptions _options;
    private readonly InferenceSession _session;

    public OnnxModelSession(string modelPath)
        : this(modelPath, OnnxSessionOptions.Cpu())
    {
    }

    // Throws OnnxDeviceUnavailableException when a DirectML session cannot be
    // created, so the caller can fall back to OnnxSessionOptions.Cpu(); a bad
    // model still throws the runtime's own failure, whatever the target.
    public OnnxModelSession(string modelPath, OnnxSessionOptions sessionOptions)
    {
        ArgumentNullException.ThrowIfNull(sessionOptions);

        bool onDevice = sessionOptions.Target == OnnxExecutionTarget.DirectMl;
        var options = new SessionOptions { ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
        try
        {
            if (onDevice)
            {
                // DirectML does not support parallel execution or the memory
                // pattern optimization; the runtime refuses the session otherwise.
                options.EnableMemoryPattern = false;
                options.AppendExecutionProvider_DML(sessionOptions.AdapterIndex);
            }
            else
            {
                options.IntraOpNumThreads = sessionOptions.ThreadCount;
                options.InterOpNumThreads = 1;
            }
            _session = new InferenceSession(modelPath, options);
        }
        catch (Exception ex)
        {
            // The ctor threw: this instance is never returned and Dispose() never
            // runs, so release the native options handle here instead of leaking it.
            options.Dispose();
            if (onDevice && ex is OnnxRuntimeException)
                throw new OnnxDeviceUnavailableException(sessionOptions.Target, ex);
            throw;
        }
        _options = options;
    }

    // The graph's inputs and outputs, in graph order.
    public IReadOnlyList<OnnxTensorInfo> Inputs => Describe(_session.InputMetadata);
    public IReadOnlyList<OnnxTensorInfo> Outputs => Describe(_session.OutputMetadata);

    private static OnnxTensorInfo[] Describe(IReadOnlyDictionary<string, NodeMetadata> metadata) =>
        metadata.Select(p => new OnnxTensorInfo(p.Key, MapElementType(p.Value), p.Value.Dimensions)).ToArray();

    private static OnnxElementType MapElementType(NodeMetadata node) => node.ElementDataType switch
    {
        TensorElementType.Float => OnnxElementType.Float,
        TensorElementType.Float16 => OnnxElementType.Float16,
        TensorElementType.Int64 => OnnxElementType.Int64,
        _ => OnnxElementType.Other,
    };

    // Runs one pass and reads the named outputs, whatever their element type:
    // Float16 comes back widened to float, int64 as long, each with its shape.
    public IReadOnlyList<OnnxTensorOutput> RunNamed(IReadOnlyList<OnnxTensorInput> inputs, IReadOnlyList<string> outputNames)
    {
        var values = new NamedOnnxValue[inputs.Count];
        for (int i = 0; i < inputs.Count; i++)
            values[i] = inputs[i].ToNamedValue();

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(values, outputNames);

        var outputs = new OnnxTensorOutput[results.Count];
        int o = 0;
        foreach (DisposableNamedOnnxValue result in results)
            outputs[o++] = ReadOutput(result);
        return outputs;
    }

    private OnnxTensorOutput ReadOutput(DisposableNamedOnnxValue result)
    {
        switch (_session.OutputMetadata[result.Name].ElementDataType)
        {
            case TensorElementType.Int64:
            {
                Tensor<long> t = result.AsTensor<long>();
                return new OnnxTensorOutput(result.Name, ShapeOf(t), null, t.ToArray());
            }
            case TensorElementType.Float16:
            {
                Tensor<Float16> t = result.AsTensor<Float16>();
                var floats = new float[t.Length];
                int i = 0;
                foreach (Float16 h in t)
                    floats[i++] = HalfConversion.ToFloat((ushort)h);
                return new OnnxTensorOutput(result.Name, ShapeOf(t), floats, null);
            }
            default:
            {
                Tensor<float> t = result.AsTensor<float>();
                return new OnnxTensorOutput(result.Name, ShapeOf(t), t.ToArray(), null);
            }
        }
    }

    private static int[] ShapeOf<T>(Tensor<T> tensor) => tensor.Dimensions.ToArray();
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

    // Same inference pass, but writes each output into a caller-owned buffer by
    // position instead of allocating a fresh float[] per output. A hot path (the
    // VAD calls this once per 512-sample window) hands in reused destination
    // buffers, so the run produces no per-window garbage: each output is read
    // straight from the runtime's DenseTensor buffer (no ToArray copy) and copied
    // into destinations[i], for the shorter of the two lengths.
    public void Run(IReadOnlyList<OnnxTensorInput> inputs, IReadOnlyList<float[]> destinations)
    {
        var values = new NamedOnnxValue[inputs.Count];
        for (int i = 0; i < inputs.Count; i++)
            values[i] = inputs[i].ToNamedValue();

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(values);

        int o = 0;
        foreach (DisposableNamedOnnxValue result in results)
        {
            ReadOnlySpan<float> src = ((DenseTensor<float>)result.AsTensor<float>()).Buffer.Span;
            float[] dest = destinations[o++];
            src.Slice(0, Math.Min(src.Length, dest.Length)).CopyTo(dest);
        }
    }

    public void Dispose()
    {
        _session.Dispose();
        _options.Dispose();
    }
}

