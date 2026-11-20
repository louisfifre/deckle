namespace Deckle.Inference.Onnx;

// One output read by name: its shape plus the data in the array that matches its
// element type. Float16 outputs arrive converted to float; int64 outputs arrive as
// long. Exactly one of Floats and Longs is set.
public sealed record OnnxTensorOutput(string Name, int[] Shape, float[]? Floats, long[]? Longs);
