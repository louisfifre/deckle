using System.IO;
using System.Text;

namespace Deckle.Inference.Onnx.Tests;

// Writes the smallest valid ONNX model by hand (protobuf wire format): one Identity
// node from input "x" to output "y" of a given element type and shape. It lets the
// session be tested on real graphs without a model file or a Python generator.
// Field numbers come from onnx.proto (ModelProto, GraphProto, NodeProto,
// ValueInfoProto, TypeProto.Tensor, TensorShapeProto).
internal static class IdentityModelFile
{
    public const int Float = 1;
    public const int Int32 = 6;
    public const int Int64 = 7;
    public const int Float16 = 10;

    // A dimension is a fixed size, or -1 for a symbolic one named "n".
    public static string Write(int elementType, params int[] dims)
    {
        string path = Path.Combine(Path.GetTempPath(), $"deckle-identity-{Guid.NewGuid():N}.onnx");
        File.WriteAllBytes(path, Build(elementType, dims));
        return path;
    }

    private static byte[] Build(int elementType, int[] dims)
    {
        byte[] node = Cat(Str(1, "x"), Str(2, "y"), Str(4, "Identity"));
        byte[] graph = Cat(Msg(1, node), Str(2, "g"), Msg(11, ValueInfo("x", elementType, dims)), Msg(12, ValueInfo("y", elementType, dims)));
        byte[] opset = Cat(Str(1, ""), Varint(2, 13));
        return Cat(Varint(1, 8), Msg(8, opset), Msg(7, graph));
    }

    private static byte[] ValueInfo(string name, int elementType, int[] dims)
    {
        byte[] shape = Cat(dims.Select(d => Msg(1, d < 0 ? Str(2, "n") : Varint(1, d))).ToArray());
        byte[] tensor = Cat(Varint(1, elementType), Msg(2, shape));
        return Cat(Str(1, name), Msg(2, Msg(1, tensor)));
    }

    private static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] Varint(int field, long value)
    {
        var bytes = new List<byte> { (byte)(field << 3) };
        ulong v = (ulong)value;
        while (v >= 0x80) { bytes.Add((byte)(v | 0x80)); v >>= 7; }
        bytes.Add((byte)v);
        return bytes.ToArray();
    }

    private static byte[] Msg(int field, byte[] body) => Cat([(byte)((field << 3) | 2)], Length(body.Length), body);

    private static byte[] Str(int field, string value) => Msg(field, Encoding.UTF8.GetBytes(value));

    private static byte[] Length(int n)
    {
        var bytes = new List<byte>();
        while (n >= 0x80) { bytes.Add((byte)(n | 0x80)); n >>= 7; }
        bytes.Add((byte)n);
        return bytes.ToArray();
    }
}
