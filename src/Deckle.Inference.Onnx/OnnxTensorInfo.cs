namespace Deckle.Inference.Onnx;

// Element types a graph port can carry. Other covers any type this module does
// not read or write yet, so metadata can still list the port.
public enum OnnxElementType
{
    Other,
    Float,
    Float16,
    Int64,
}

// One input or output of a graph: name, element type and shape. A dynamic or
// symbolic dimension reads -1.
public sealed record OnnxTensorInfo(string Name, OnnxElementType ElementType, int[] Shape);
