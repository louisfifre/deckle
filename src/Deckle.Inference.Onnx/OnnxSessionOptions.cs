namespace Deckle.Inference.Onnx;

// How an OnnxModelSession is created: the execution target and its knobs. The
// default is the policy a small model wants (CPU, one thread), which Silero
// relies on; the factories name the other shapes so a call site reads as intent.
public sealed record OnnxSessionOptions
{
    public OnnxExecutionTarget Target { get; private init; } = OnnxExecutionTarget.Cpu;

    // CPU only: threads used inside one operator (and across operators, which stay
    // sequential). A large encoder on the CPU needs more than the default one.
    public int ThreadCount { get; private init; } = 1;

    // DirectML only: index of the adapter in the DXGI enumeration order.
    public int AdapterIndex { get; private init; }

    public static OnnxSessionOptions Cpu(int threadCount = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threadCount, 1);
        return new OnnxSessionOptions { Target = OnnxExecutionTarget.Cpu, ThreadCount = threadCount };
    }

    public static OnnxSessionOptions DirectMl(int adapterIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(adapterIndex);
        return new OnnxSessionOptions { Target = OnnxExecutionTarget.DirectMl, AdapterIndex = adapterIndex };
    }
}
