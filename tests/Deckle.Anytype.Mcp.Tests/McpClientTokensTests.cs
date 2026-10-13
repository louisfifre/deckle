using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// MaterializeEnvironmentVariables stays untested on purpose: it writes real User
// environment variables and broadcasts WM_SETTINGCHANGE to every top-level window,
// so exercising it would mutate the developer's machine.
[Trait("Category", "unit")]
public class McpClientTokensTests
{
    static (McpClientTokens Tokens, FakeSecretVault Vault) Minted()
    {
        var vault = new FakeSecretVault();
        var tokens = new McpClientTokens(vault);
        tokens.EnsureMinted();
        return (tokens, vault);
    }

    [Fact]
    public void EnsureMintedMintsOneTokenPerClient()
    {
        var (_, vault) = Minted();

        foreach (var client in McpClients.All)
        {
            Assert.True(vault.TryGet(client.TokenSecretName, out string? token));
            Assert.False(string.IsNullOrEmpty(token));
        }
    }

    [Fact]
    public void EnsureMintedIsIdempotent()
    {
        var (tokens, vault) = Minted();

        // A boot that finds the tokens present must not rotate them.
        var before = McpClients.All.ToDictionary(
            c => c.TokenSecretName,
            c => { vault.TryGet(c.TokenSecretName, out string? t); return t; });

        tokens.EnsureMinted();

        foreach (var client in McpClients.All)
        {
            vault.TryGet(client.TokenSecretName, out string? after);
            Assert.Equal(before[client.TokenSecretName], after);
        }
    }

    [Fact]
    public void MintedTokensAreBase64UrlWithoutPaddingOrUnsafeChars()
    {
        var (_, vault) = Minted();

        foreach (var client in McpClients.All)
        {
            vault.TryGet(client.TokenSecretName, out string? token);
            // base64url swaps + / for - _ and drops = padding, so the token rides an
            // Authorization header and a shell env var without escaping.
            Assert.DoesNotContain('+', token!);
            Assert.DoesNotContain('/', token!);
            Assert.DoesNotContain('=', token!);
        }
    }

    [Fact]
    public void AuthenticateResolvesEachClientsOwnToken()
    {
        var (tokens, vault) = Minted();

        foreach (var client in McpClients.All)
        {
            vault.TryGet(client.TokenSecretName, out string? token);
            McpClientProfile? resolved = tokens.Authenticate(token);

            Assert.NotNull(resolved);
            Assert.Same(client, resolved);
        }
    }

    [Fact]
    public void AuthenticateNeverCrossMatchesBetweenClients()
    {
        var (tokens, vault) = Minted();

        // The token is the identity: a bearer must never authenticate as another client.
        vault.TryGet(McpClients.Claude.TokenSecretName, out string? claudeToken);
        vault.TryGet(McpClients.Codex.TokenSecretName, out string? codexToken);
        vault.TryGet(McpClients.SchemaAdmin.TokenSecretName, out string? schemaToken);

        Assert.Same(McpClients.Claude, tokens.Authenticate(claudeToken));
        Assert.Same(McpClients.Codex, tokens.Authenticate(codexToken));
        Assert.Same(McpClients.SchemaAdmin, tokens.Authenticate(schemaToken));
        Assert.NotSame(McpClients.Codex, tokens.Authenticate(claudeToken));
        Assert.NotSame(McpClients.Claude, tokens.Authenticate(codexToken));
        Assert.NotSame(McpClients.SchemaAdmin, tokens.Authenticate(claudeToken));
        Assert.NotSame(McpClients.Claude, tokens.Authenticate(schemaToken));
    }

    [Fact]
    public void ConfiguredCustomClientIsMintedAndAuthenticated()
    {
        var custom = new McpClientProfile(
            "custom",
            McpClients.Codex.Surface,
            "mcp-token-custom",
            "DECKLE_MCP_TOKEN_CUSTOM");
        var vault = new FakeSecretVault();
        var tokens = new McpClientTokens(vault, [.. McpClients.All, custom]);

        tokens.EnsureMinted();
        vault.TryGet(custom.TokenSecretName, out string? token);

        Assert.False(string.IsNullOrEmpty(token));
        Assert.Same(custom, tokens.Authenticate(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public void AuthenticateRejectsBlankAndUnknownBearers(string? bearer)
    {
        var (tokens, _) = Minted();

        Assert.Null(tokens.Authenticate(bearer));
    }
}
