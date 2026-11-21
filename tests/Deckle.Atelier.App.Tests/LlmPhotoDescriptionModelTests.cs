using System.Net.Http;
using Deckle.Atelier;
using Deckle.Llm;
using Xunit;

namespace Deckle.Atelier.App.Tests;

// The adapter between the Atelier's description seam and Deckle.Llm: it reads
// the one stored choice, sends only to a provider on this machine, and words
// each failure as a kind a view can say. The engine is a fake: no server runs.
[Trait("Category", "unit")]
public sealed class LlmPhotoDescriptionModelTests
{
    static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xD9];

    static readonly LlmProvider LocalOllama = new()
    {
        Id = "local", Name = "Ollama", Kind = LlmProviderKind.Ollama, Endpoint = "http://localhost:11434/api/generate",
    };

    static LlmPhotoDescriptionModel Model(
        FakeEngine engine, PhotoDescriptionModelChoice? choice, params LlmProvider[] providers) =>
        new(() => choice, () => providers.Length == 0 ? [LocalOllama] : providers, new FakeResolver(engine));

    static readonly PhotoDescriptionModelChoice Chosen = new("local", "vision-model");

    [Fact]
    public async Task ADescriptionCarriesTheModelThatWroteItAndThePictureWentAsAnImage()
    {
        var engine = new FakeEngine(_ => new GenerationResult("Un chat sur un mur.", 0, 0, 0, 0, 0, 0));

        PhotoDescriptionResult result = await Model(engine, Chosen).DescribeAsync(Jpeg, "prompt", CancellationToken.None);

        Assert.Equal("Un chat sur un mur.", result.Description?.Text);
        Assert.Equal("vision-model", result.Description?.Model);
        GenerationRequest sent = Assert.Single(engine.Requests);
        Assert.Equal("vision-model", sent.Model);
        Assert.Equal("prompt", sent.UserText);
        Assert.Same(Jpeg, sent.Image?.Bytes);
        Assert.Equal("image/jpeg", sent.Image?.MediaType);
    }

    // What the panel and the folder command read before offering a button:
    // the same answer a request would get, without asking a server.
    [Fact]
    public void ReadinessSaysWhatIsMissingWithoutCallingTheEngine()
    {
        var engine = new FakeEngine(_ => throw new InvalidOperationException("never called"));
        var remote = new LlmProvider { Id = "local", Kind = LlmProviderKind.OpenAiCompatible, Endpoint = "https://api.mistral.ai/v1" };

        Assert.Null(Model(engine, Chosen).Readiness());
        Assert.Equal(PhotoDescriptionFailure.NoModelChosen, Model(engine, null).Readiness());
        Assert.Equal(PhotoDescriptionFailure.EndpointNotLocal, Model(engine, Chosen, remote).Readiness());
        Assert.Empty(engine.Requests);
    }

    [Fact]
    public async Task NothingChosenIsNoModelChosen()
    {
        var engine = new FakeEngine(_ => throw new InvalidOperationException("never called"));

        PhotoDescriptionResult result = await Model(engine, null).DescribeAsync(Jpeg, "p", CancellationToken.None);

        Assert.Equal(PhotoDescriptionFailure.NoModelChosen, result.Failure);
        Assert.Empty(engine.Requests);
    }

    [Fact]
    public async Task AChoiceWhoseProviderIsGoneOrOffIsNoModelChosen()
    {
        var engine = new FakeEngine(_ => throw new InvalidOperationException("never called"));
        var off = new LlmProvider { Id = "local", Kind = LlmProviderKind.Ollama, Endpoint = "http://localhost:11434", Enabled = false };

        Assert.Equal(PhotoDescriptionFailure.NoModelChosen,
            (await Model(engine, new("gone", "m")).DescribeAsync(Jpeg, "p", CancellationToken.None)).Failure);
        Assert.Equal(PhotoDescriptionFailure.NoModelChosen,
            (await Model(engine, Chosen, off).DescribeAsync(Jpeg, "p", CancellationToken.None)).Failure);
        Assert.Empty(engine.Requests);
    }

    [Theory]
    [InlineData(LlmProviderKind.Claude, "")]
    [InlineData(LlmProviderKind.OpenAiCompatible, "https://api.mistral.ai/v1")]
    [InlineData(LlmProviderKind.Ollama, "http://192.168.1.20:11434/api/generate")]
    public async Task AProviderNotOnThisMachineGetsNoPicture(LlmProviderKind kind, string endpoint)
    {
        var engine = new FakeEngine(_ => throw new InvalidOperationException("never called"));
        var remote = new LlmProvider { Id = "local", Kind = kind, Endpoint = endpoint };

        PhotoDescriptionResult result = await Model(engine, Chosen, remote).DescribeAsync(Jpeg, "p", CancellationToken.None);

        Assert.Equal(PhotoDescriptionFailure.EndpointNotLocal, result.Failure);
        Assert.Empty(engine.Requests);
    }

    public static TheoryData<string, PhotoDescriptionFailure> Throws => new()
    {
        { "connection", PhotoDescriptionFailure.ServerUnreachable },
        { "status", PhotoDescriptionFailure.Other },
        { "no-images", PhotoDescriptionFailure.ImagesNotSupported },
        { "not-local", PhotoDescriptionFailure.EndpointNotLocal },
        { "json", PhotoDescriptionFailure.Other },
    };

    [Theory]
    [MemberData(nameof(Throws))]
    public async Task EachEngineFailureHasItsKind(string thrown, PhotoDescriptionFailure expected)
    {
        var engine = new FakeEngine(_ => throw (thrown switch
        {
            "connection" => new HttpRequestException("No connection could be made"),
            "status" => new HttpRequestException("500", null, System.Net.HttpStatusCode.InternalServerError),
            "no-images" => new ImageInputNotSupportedException("does not support images"),
            "not-local" => new LocalEndpointRequiredException(),
            _ => (Exception)new System.Text.Json.JsonException(),
        }));

        PhotoDescriptionResult result = await Model(engine, Chosen).DescribeAsync(Jpeg, "p", CancellationToken.None);

        Assert.Equal(expected, result.Failure);
    }

    [Fact]
    public async Task AnEmptyAnswerIsAFailure()
    {
        var engine = new FakeEngine(_ => new GenerationResult("  ", 0, 0, 0, 0, 0, 0));

        Assert.Equal(PhotoDescriptionFailure.Other,
            (await Model(engine, Chosen).DescribeAsync(Jpeg, "p", CancellationToken.None)).Failure);
    }

    [Fact]
    public async Task StoppedByThePersonIsStopped()
    {
        using var stop = new CancellationTokenSource();
        var engine = new FakeEngine(ct =>
        {
            stop.Cancel();
            ct.ThrowIfCancellationRequested();
            return default;
        });

        Assert.Equal(PhotoDescriptionFailure.Stopped,
            (await Model(engine, Chosen).DescribeAsync(Jpeg, "p", stop.Token)).Failure);
    }

    [Fact]
    public async Task APhotoThatOutlivesItsDeadlineFailsWithoutBeingStopped()
    {
        var engine = new FakeEngine(ct =>
        {
            Task.Delay(Timeout.Infinite, ct).GetAwaiter().GetResult();
            return default;
        });
        var model = new LlmPhotoDescriptionModel(() => Chosen, () => [LocalOllama], new FakeResolver(engine),
            deadline: TimeSpan.FromMilliseconds(50));

        Assert.Equal(PhotoDescriptionFailure.Other,
            (await model.DescribeAsync(Jpeg, "p", CancellationToken.None)).Failure);
    }

    sealed class FakeEngine(Func<CancellationToken, GenerationResult> answer) : IGenerationEngine
    {
        public List<GenerationRequest> Requests { get; } = [];

        public GenerationResult Generate(GenerationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return answer(cancellationToken);
        }
    }

    sealed class FakeResolver(IGenerationEngine engine) : IGenerationEngineResolver
    {
        public IGenerationEngine? Resolve(string providerId) => engine;
    }
}
