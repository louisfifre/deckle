using System;

namespace Deckle.Inference.Onnx;

// The requested device could not host the session (no adapter at that index, no
// DirectML runtime, a graph the device rejects). Distinct from a malformed model,
// which throws as before: a caller catches this one to retry on the CPU.
public sealed class OnnxDeviceUnavailableException : Exception
{
    public OnnxDeviceUnavailableException(OnnxExecutionTarget target, Exception inner)
        : base($"The {target} execution provider could not create the session: {inner.Message}", inner)
    {
        Target = target;
    }

    public OnnxExecutionTarget Target { get; }
}
