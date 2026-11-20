namespace Deckle.Inference.Onnx;

// Where a session runs. Chosen once at creation; the module owns the policy that
// each choice implies so a caller never sets runtime flags itself.
public enum OnnxExecutionTarget
{
    Cpu,
    DirectMl,
}
