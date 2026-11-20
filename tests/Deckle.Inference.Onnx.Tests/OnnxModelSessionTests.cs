using System.IO;
using Xunit;

namespace Deckle.Inference.Onnx.Tests;

// OnnxModelSession on generated Identity graphs: each element type goes in and
// comes out by name, the graph describes itself, and the CPU and DirectML targets
// create sessions. The runtime is the real one, the models are tiny and in memory.
[Trait("Category", "integration")]
public class OnnxModelSessionTests : IDisposable
{
    private readonly List<string> _files = [];

    private string Model(int elementType, params int[] dims)
    {
        string path = IdentityModelFile.Write(elementType, dims);
        _files.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string f in _files)
            File.Delete(f);
    }

    [Fact]
    public void FloatTensorRoundTripsByName()
    {
        using var session = new OnnxModelSession(Model(IdentityModelFile.Float, 2, 2));

        var outputs = session.RunNamed([OnnxTensorInput.Float("x", [1f, 2f, 3f, 4f], [2, 2])], ["y"]);

        OnnxTensorOutput y = Assert.Single(outputs);
        Assert.Equal("y", y.Name);
        Assert.Equal([2, 2], y.Shape);
        Assert.Equal([1f, 2f, 3f, 4f], y.Floats!);
        Assert.Null(y.Longs);
    }

    [Fact]
    public void Float16TensorIsFedAndReadBackAsFloat()
    {
        using var session = new OnnxModelSession(Model(IdentityModelFile.Float16, 3));

        var outputs = session.RunNamed([OnnxTensorInput.Float16("x", [1.5f, -2f, 0.25f], [3])], ["y"]);

        Assert.Equal([1.5f, -2f, 0.25f], outputs[0].Floats!);
    }

    [Fact]
    public void Int64TensorIsReadBackAsLong()
    {
        using var session = new OnnxModelSession(Model(IdentityModelFile.Int64, 2));

        var outputs = session.RunNamed([OnnxTensorInput.Int64("x", [5_000_000_000L, -3L], [2])], ["y"]);

        Assert.Equal([5_000_000_000L, -3L], outputs[0].Longs!);
        Assert.Null(outputs[0].Floats!);
    }

    [Fact]
    public void Int32TensorRoundTripsAsLong()
    {
        using var session = new OnnxModelSession(Model(IdentityModelFile.Int32, 3));

        var outputs = session.RunNamed([OnnxTensorInput.Int32("x", [8192, 0, -1], [3])], ["y"]);

        Assert.Equal([8192L, 0L, -1L], outputs[0].Longs!);
        Assert.Null(outputs[0].Floats);
    }

    [Fact]
    public void GraphMetadataNamesTypesAndShapes()
    {
        using var session = new OnnxModelSession(Model(IdentityModelFile.Float16, -1, 4));

        OnnxTensorInfo input = Assert.Single(session.Inputs);
        OnnxTensorInfo output = Assert.Single(session.Outputs);
        Assert.Equal(new OnnxTensorInfo("x", OnnxElementType.Float16, [-1, 4]).Name, input.Name);
        Assert.Equal(OnnxElementType.Float16, input.ElementType);
        Assert.Equal([-1, 4], input.Shape);
        Assert.Equal("y", output.Name);
    }

    [Fact]
    public void CpuSessionAcceptsMoreThanOneThread()
    {
        using var session = new OnnxModelSession(Model(IdentityModelFile.Float, 1), OnnxSessionOptions.Cpu(4));

        var outputs = session.RunNamed([OnnxTensorInput.Float("x", [7f], [1])], ["y"]);

        Assert.Equal([7f], outputs[0].Floats!);
    }

    [Fact]
    public void DestinationsOverloadFillsTheCallersBufferWithoutAnotherArray()
    {
        using var session = new OnnxModelSession(Model(IdentityModelFile.Float, 2));
        var destination = new float[2];

        session.Run([OnnxTensorInput.Float("x", [8f, 9f], [2])], [destination]);

        Assert.Equal([8f, 9f], destination);
    }

    [Fact]
    public void DirectMlOnAnAdapterThatDoesNotExistReportsTheDeviceAsUnavailable()
    {
        string model = Model(IdentityModelFile.Float, 1);

        var ex = Assert.Throws<OnnxDeviceUnavailableException>(
            () => new OnnxModelSession(model, OnnxSessionOptions.DirectMl(adapterIndex: 999)));

        Assert.Equal(OnnxExecutionTarget.DirectMl, ex.Target);
    }

    [Fact]
    public void DirectMlSessionRunsOnTheDefaultAdapterOrReportsItUnavailable()
    {
        string model = Model(IdentityModelFile.Float, 1);
        OnnxModelSession session;
        try
        {
            session = new OnnxModelSession(model, OnnxSessionOptions.DirectMl());
        }
        catch (OnnxDeviceUnavailableException)
        {
            return; // A machine without a DirectML adapter: the fallback signal is the contract.
        }

        using (session)
        {
            Assert.Equal([3f], session.RunNamed([OnnxTensorInput.Float("x", [3f], [1])], ["y"])[0].Floats!);
        }
    }

    [Fact]
    public void MalformedModelStillThrowsTheRuntimeFailureNotADeviceError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"deckle-bad-{Guid.NewGuid():N}.onnx");
        File.WriteAllBytes(path, [1, 2, 3]);
        _files.Add(path);

        Assert.NotNull(Record.Exception(() => new OnnxModelSession(path, OnnxSessionOptions.Cpu())));
        Assert.IsNotType<OnnxDeviceUnavailableException>(Record.Exception(() => new OnnxModelSession(path)));
    }
}

