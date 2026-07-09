using Deckle.Anytype.Mcp;
using Xunit;

namespace Deckle.Anytype.Mcp.Tests;

// Unit tests for McpClientTokens against a FakeSecretVault: minting, idempotence,
// the base64url shape, and the constant-time Authenticate that maps a bearer back
// to its own client and no other. No environment variables and no network — the
// vault is entirely in memory.
//
// MaterializeEnvironmentVariables is deliberately NOT tested: it writes real User
// environment variables and broadcasts WM_SETTINGCHANGE to every top-level window,
// so exercising it would mutate the developer's machine. A unit test must not do
// that; its behaviour is left to manual/integration verification.
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

    // ── minting ───────────────────────────────────────────────────────────────

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

        // Snapshot the first mint, run provisioning again, and prove nothing moved:
        // a boot that finds the tokens present must not rotate them.
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

    // ── authentication ──────────────────────────────────────────────────────────

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

        // Each bearer must authenticate as its own client and nothing else. This is the whole security model —
        // the token IS the identity.
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
