using Xunit;

namespace Deckle.Transcription.Parakeet.Tests.Backend;

// The lexicon through the backend, on the fake model that says "hello world.".
[Trait("Category", "unit")]
public sealed class ParakeetBackendLexiconTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static float[] Audio(double seconds) => new float[(int)(seconds * 16_000)];

    private static async Task<ParakeetBackend> LoadedAsync(FakeHost host)
    {
        var backend = new ParakeetBackend(host, FakeLoader.Succeeding());
        Assert.True((await backend.LoadModelAsync(Ct)).Success);
        return backend;
    }

    [Fact]
    public async Task TheTextAndTheSinkCarryTheCasingOfTheTerms()
    {
        var host = new FakeHost();
        host.Transcription.Lexicon.Terms = ["Hello", "WORLD"];
        using var backend = await LoadedAsync(host);
        var sunk = new List<TranscriptionSegment>();

        TranscriptionResult result = await backend.TranscribeAsync(Audio(1), sunk.Add, Ct);

        Assert.Equal("Hello WORLD.", result.FullText);
        Assert.Equal("Hello WORLD.", Assert.Single(sunk).Text);
    }

    [Fact]
    public async Task AnEditToTheLexiconAppliesAtTheNextCall()
    {
        var host = new FakeHost();
        host.Transcription.Lexicon.Terms = ["Hello"];
        using var backend = await LoadedAsync(host);

        string first = (await backend.TranscribeAsync(Audio(1), null, Ct)).FullText;
        host.Transcription.Lexicon.Terms = [];
        string second = (await backend.TranscribeAsync(Audio(1), null, Ct)).FullText;

        Assert.Equal("Hello world.", first);
        Assert.Equal("hello world.", second);
    }

    [Fact]
    public async Task TheBoostSwitchLeavesTheCasingPassOn()
    {
        var host = new FakeHost();
        host.Transcription.Lexicon.Terms = ["Hello"];
        host.Transcription.Lexicon.BoostEnabled = false;
        using var backend = await LoadedAsync(host);

        Assert.Equal("Hello world.", (await backend.TranscribeAsync(Audio(1), null, Ct)).FullText);
    }
}
