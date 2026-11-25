using Xunit;

namespace Deckle.Llm.Tests;

// A mistyped Ollama endpoint is an error the Rewriting page can show, never a
// quiet switch to the default address.
[Trait("Category", "unit")]
public sealed class OllamaServiceEndpointTests
{
    [Theory]
    [InlineData("localhost:11434")]
    [InlineData("ftp://192.168.0.1:11434")]
    [InlineData("not an address")]
    [InlineData("")]
    public async Task AMistypedEndpointIsNotAvailableAndListingItFails(string endpoint)
    {
        var service = new OllamaService(() => endpoint);

        Assert.False(await service.IsAvailableAsync());
        await Assert.ThrowsAsync<UriFormatException>(
            () => service.ListModelsAsync(TestContext.Current.CancellationToken));
    }
}
