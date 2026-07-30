using Deckle.Autocorrect.Probe;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class QwenAdapterCompatibilityTests
{
    [Fact]
    public void BuildsTheFrozenQwen06BLoRaContractInDeterministicOrder()
    {
        QwenAdapterGraphContract contract = QwenAdapterGraphContract.Create(
            layerCount: 28,
            inputSize: 1024,
            queryOutputSize: 2048,
            valueOutputSize: 1024,
            rank: 8);

        Assert.Equal(112, contract.NodeNames.Count);
        Assert.Equal(112, contract.Tensors.Count);
        Assert.Equal(
            "/model/layers.0/attn/q_proj/lora_A/MatMul",
            contract.NodeNames[0]);
        Assert.Equal(
            "/model/layers.0/attn/q_proj/lora_B/MatMul",
            contract.NodeNames[1]);
        Assert.Equal(
            "/model/layers.0/attn/v_proj/lora_A/MatMul",
            contract.NodeNames[2]);
        Assert.Equal(
            "/model/layers.0/attn/v_proj/lora_B/MatMul",
            contract.NodeNames[3]);
        Assert.Equal(
            "/model/layers.27/attn/v_proj/lora_B/MatMul",
            contract.NodeNames[^1]);

        AssertTensor(
            contract.Tensors[0],
            "model.layers.0.attn.q_proj.lora_A.MatMul.weight",
            [1024, 8]);
        AssertTensor(
            contract.Tensors[1],
            "model.layers.0.attn.q_proj.lora_B.MatMul.weight",
            [8, 2048]);
        AssertTensor(
            contract.Tensors[2],
            "model.layers.0.attn.v_proj.lora_A.MatMul.weight",
            [1024, 8]);
        AssertTensor(
            contract.Tensors[3],
            "model.layers.0.attn.v_proj.lora_B.MatMul.weight",
            [8, 1024]);
    }

    [Theory]
    [InlineData(0, 1024, 2048, 1024, 8)]
    [InlineData(28, 0, 2048, 1024, 8)]
    [InlineData(28, 1024, 0, 1024, 8)]
    [InlineData(28, 1024, 2048, 0, 8)]
    [InlineData(28, 1024, 2048, 1024, 0)]
    public void RejectsIncompleteGraphDimensions(
        int layers,
        int input,
        int queryOutput,
        int valueOutput,
        int rank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QwenAdapterGraphContract.Create(
                layers,
                input,
                queryOutput,
                valueOutput,
                rank));
    }

    [Fact]
    public void AcceptsAnExactSemanticAndStructuralManifest()
    {
        QwenAdapterManifest expected = ExactManifest();

        QwenAdapterManifestVerdict verdict =
            QwenAdapterManifestGate.Evaluate(expected, expected);

        Assert.True(verdict.Accepted);
        Assert.Equal(QwenAdapterManifestVerdict.AcceptedCode, verdict.Code);
    }

    [Fact]
    public void RejectsEveryFrozenManifestMismatchBeforeRuntime()
    {
        foreach ((string expectedCode, QwenAdapterManifest actual) in MismatchedManifests())
        {
            var loader = new RecordingAdapterLoader();

            QwenAdapterLoadOutcome outcome = QwenAdapterPolicyLoader.TryLoad(
                ExactManifest(),
                actual,
                "adapter.onnx_adapter",
                "sentinel",
                loader);

            Assert.False(outcome.Loaded);
            Assert.Equal(QwenAdapterLoadStage.ProbePolicy, outcome.Stage);
            Assert.Equal(expectedCode, outcome.Code);
            Assert.Equal(0, loader.LoadCalls);
        }
    }

    [Fact]
    public void KeepsRuntimeFailureDistinctFromProbePolicyRefusal()
    {
        var loader = new RecordingAdapterLoader
        {
            Exception = new InvalidOperationException("wrong tensor shape"),
        };

        QwenAdapterLoadOutcome outcome = QwenAdapterPolicyLoader.TryLoad(
            ExactManifest(),
            ExactManifest(),
            "adapter.onnx_adapter",
            "sentinel",
            loader);

        Assert.False(outcome.Loaded);
        Assert.Equal(QwenAdapterLoadStage.Runtime, outcome.Stage);
        Assert.Equal(QwenAdapterLoadOutcome.RuntimeLoadFailedCode, outcome.Code);
        Assert.Equal("InvalidOperationException", outcome.ExceptionType);
        Assert.Equal(1, loader.LoadCalls);
    }

    [Fact]
    public void LoadsOnlyAfterTheExactManifestPasses()
    {
        var loader = new RecordingAdapterLoader();

        QwenAdapterLoadOutcome outcome = QwenAdapterPolicyLoader.TryLoad(
            ExactManifest(),
            ExactManifest(),
            "adapter.onnx_adapter",
            "control",
            loader);

        Assert.True(outcome.Loaded);
        Assert.Equal(QwenAdapterLoadStage.Runtime, outcome.Stage);
        Assert.Equal(QwenAdapterLoadOutcome.LoadedCode, outcome.Code);
        Assert.Equal(1, loader.LoadCalls);
        Assert.Equal("adapter.onnx_adapter", loader.LastPath);
        Assert.Equal("control", loader.LastName);
    }

    private static IReadOnlyList<(string Code, QwenAdapterManifest Manifest)>
        MismatchedManifests()
    {
        QwenAdapterManifest exact = ExactManifest();
        QwenAdapterTensorContract[] tensors = exact.Tensors.ToArray();

        return
        [
            (
                QwenAdapterManifestVerdict.InvalidManifestCode,
                exact with { GraphSha256 = "not-a-sha" }
            ),
            (
                QwenAdapterManifestVerdict.BaseRepositoryMismatchCode,
                exact with { BaseRepository = "Qwen/wrong" }
            ),
            (
                QwenAdapterManifestVerdict.BaseRevisionMismatchCode,
                exact with { BaseRevision = new string('b', 40) }
            ),
            (
                QwenAdapterManifestVerdict.GraphHashMismatchCode,
                exact with { GraphSha256 = new string('b', 64) }
            ),
            (
                QwenAdapterManifestVerdict.TargetModulesMismatchCode,
                exact with { TargetModules = ["v_proj", "q_proj"] }
            ),
            (
                QwenAdapterManifestVerdict.RankMismatchCode,
                exact with { Rank = 16 }
            ),
            (
                QwenAdapterManifestVerdict.DTypeMismatchCode,
                exact with
                {
                    DType = "float32",
                    Tensors = tensors
                        .Select(static tensor => tensor with { DType = "float32" })
                        .ToArray(),
                }
            ),
            (
                QwenAdapterManifestVerdict.TensorCountMismatchCode,
                exact with { Tensors = tensors[..^1] }
            ),
            (
                QwenAdapterManifestVerdict.TensorNameMismatchCode,
                exact with
                {
                    Tensors =
                    [
                        tensors[0] with { Name = "wrong" },
                        .. tensors[1..],
                    ],
                }
            ),
            (
                QwenAdapterManifestVerdict.TensorShapeMismatchCode,
                exact with
                {
                    Tensors =
                    [
                        tensors[0] with { Shape = [1, 8] },
                        .. tensors[1..],
                    ],
                }
            ),
            (
                QwenAdapterManifestVerdict.TensorDTypeMismatchCode,
                exact with
                {
                    Tensors =
                    [
                        tensors[0] with { DType = "float32" },
                        .. tensors[1..],
                    ],
                }
            ),
        ];
    }

    private static QwenAdapterManifest ExactManifest()
    {
        QwenAdapterGraphContract graph = QwenAdapterGraphContract.Create(
            1,
            1024,
            2048,
            1024,
            8);

        return new QwenAdapterManifest(
            "Qwen/Qwen3-0.6B",
            new string('a', 40),
            new string('a', 64),
            ["q_proj", "v_proj"],
            8,
            "float16",
            graph.Tensors);
    }

    private static void AssertTensor(
        QwenAdapterTensorContract tensor,
        string expectedName,
        int[] expectedShape)
    {
        Assert.Equal(expectedName, tensor.Name);
        Assert.Equal(expectedShape, tensor.Shape);
        Assert.Equal("float16", tensor.DType);
    }

    private sealed class RecordingAdapterLoader : IQwenAdapterLoader
    {
        public Exception? Exception { get; init; }
        public int LoadCalls { get; private set; }
        public string? LastPath { get; private set; }
        public string? LastName { get; private set; }

        public void LoadAdapter(string path, string name)
        {
            LoadCalls++;
            LastPath = path;
            LastName = name;
            if (Exception is not null)
                throw Exception;
        }
    }
}
