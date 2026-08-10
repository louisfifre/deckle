using Deckle.Anytype;
using Deckle.Anytype.Mcp;
using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

[Trait("Category", "unit")]
public class HomeMcpTests
{
    [Fact]
    public void ClientDeclaresItsOwnSurfaceAndProvisioningCoordinates()
    {
        Assert.Equal("home", HomeMcp.Client.Id);
        Assert.Equal("home", HomeMcp.Client.Surface.Id);
        Assert.Equal("mcp-token-home", HomeMcp.Client.TokenSecretName);
        Assert.Equal("DECKLE_MCP_TOKEN_HOME", HomeMcp.Client.TokenEnvVar);
    }

    [Fact]
    public void SurfaceOpensWithOnlyHomeToolsWithoutResolvingRuntimeConfiguration()
    {
        using var api = new AnytypeApiClient(new AnytypeCredentials(
            "http://127.0.0.1:1", "2025-05-20", "dummy-key", "dummy-space"));

        McpSurfaceBinding surface = HomeMcp.Client.Surface.Open(api);

        Assert.Equal(
            new[]
            {
                "chantier_create", "chantier_overview", "complete", "create",
                "delete", "get", "search", "tache_create", "update",
            },
            surface.Tools.Select(tool => tool.Name).OrderBy(name => name));
        Assert.Contains("home inventory", surface.Descriptor.Instructions);
    }
}
