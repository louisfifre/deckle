namespace Deckle.Autocorrect.Probe;

internal sealed record QwenAdapterTensorContract(
    string Name,
    IReadOnlyList<int> Shape,
    string DType);

internal sealed class QwenAdapterGraphContract
{
    private QwenAdapterGraphContract(
        IReadOnlyList<string> nodeNames,
        IReadOnlyList<QwenAdapterTensorContract> tensors)
    {
        NodeNames = nodeNames;
        Tensors = tensors;
    }

    public IReadOnlyList<string> NodeNames { get; }
    public IReadOnlyList<QwenAdapterTensorContract> Tensors { get; }

    public static QwenAdapterGraphContract Create(
        int layerCount,
        int inputSize,
        int queryOutputSize,
        int valueOutputSize,
        int rank)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(layerCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(queryOutputSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(valueOutputSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rank, 1);

        var nodes = new List<string>(layerCount * 4);
        var tensors = new List<QwenAdapterTensorContract>(layerCount * 4);
        for (int layer = 0; layer < layerCount; layer++)
        {
            AddProjection(
                nodes,
                tensors,
                layer,
                "q_proj",
                inputSize,
                queryOutputSize,
                rank);
            AddProjection(
                nodes,
                tensors,
                layer,
                "v_proj",
                inputSize,
                valueOutputSize,
                rank);
        }

        return new QwenAdapterGraphContract(nodes.AsReadOnly(), tensors.AsReadOnly());
    }

    private static void AddProjection(
        ICollection<string> nodes,
        ICollection<QwenAdapterTensorContract> tensors,
        int layer,
        string projection,
        int inputSize,
        int outputSize,
        int rank)
    {
        AddFactor(nodes, tensors, layer, projection, "lora_A", inputSize, rank);
        AddFactor(nodes, tensors, layer, projection, "lora_B", rank, outputSize);
    }

    private static void AddFactor(
        ICollection<string> nodes,
        ICollection<QwenAdapterTensorContract> tensors,
        int layer,
        string projection,
        string factor,
        int rows,
        int columns)
    {
        string nodeName = $"/model/layers.{layer}/attn/{projection}/{factor}/MatMul";
        nodes.Add(nodeName);
        tensors.Add(new QwenAdapterTensorContract(
            nodeName[1..].Replace('/', '.') + ".weight",
            [rows, columns],
            "float16"));
    }
}
