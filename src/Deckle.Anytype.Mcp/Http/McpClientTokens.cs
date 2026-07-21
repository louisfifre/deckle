using System.Security.Cryptography;
using System.Text;
using Deckle.Security;

namespace Deckle.Anytype.Mcp;

// The per-client bearer tokens: minting, publishing to the environment, and the
// constant-time check the host runs on every request. The token IS the identity —
// it decides which client profile a request is served as — so it never touches a
// log, and the equality check runs in fixed time so a mismatch leaks nothing about
// where it diverged.
//
// The token lives in two places by necessity. The vault is the source of truth,
// sealed to the current Windows user. The User environment variable is the copy the
// external client process reads by name; it must exist for the client to connect,
// but it is a mirror, never the authority — Authenticate compares against a
// once-per-process snapshot of the vault, never against the environment.
public sealed class McpClientTokens
{
    private readonly ISecretVault _vault;
    private readonly IReadOnlyList<McpClientProfile> _clients;

    public McpClientTokens(
        ISecretVault vault,
        IReadOnlyList<McpClientProfile>? clients = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        _vault = vault;
        _clients = clients?.ToArray() ?? McpClients.All;

        RequireUnique(_clients.Select(client => client.Id), "client id");
        RequireUnique(_clients.Select(client => client.TokenSecretName), "token secret name");
        RequireUnique(_clients.Select(client => client.TokenEnvVar), "token environment variable");
    }

    // The vault's tokens, decoded once and held for the host's lifetime. Tokens
    // never rotate at runtime (minting happens before the host starts, rotation
    // is a restart), so re-reading the file and re-running DPAPI on every single
    // request would buy nothing and cost a decrypt per auth — twice on a miss.
    // Invalidated by EnsureMinted so a first-boot mint is picked up in-process.
    private volatile IReadOnlyList<(McpClientProfile Client, byte[] Token)>? _snapshot;

    // Mint any client bearer the vault is still missing. Idempotent: a token already
    // present is left untouched, so provisioning that runs on every boot only writes
    // on the very first one. A fresh token is 32 random bytes in base64url without
    // padding — URL-safe so it rides an Authorization header and a shell env var
    // without escaping, and never logged on the way out.
    public void EnsureMinted()
    {
        foreach (var client in _clients)
        {
            if (!_vault.Contains(client.TokenSecretName))
                _vault.Set(client.TokenSecretName, Mint());
        }

        _snapshot = null;
    }

    // Publish each vault token to its User environment variable so the external
    // client process can read it by name. The write is skipped when the variable
    // already holds the right value: SetEnvironmentVariable at User scope broadcasts
    // WM_SETTINGCHANGE to every top-level window, and a boot that changed nothing
    // should stay silent rather than nudge the whole desktop.
    public void MaterializeEnvironmentVariables()
    {
        foreach (var client in _clients)
        {
            if (!_vault.TryGet(client.TokenSecretName, out string? token) || token is null)
                continue;

            string? current = Environment.GetEnvironmentVariable(
                client.TokenEnvVar, EnvironmentVariableTarget.User);
            if (current == token)
                continue;

            Environment.SetEnvironmentVariable(
                client.TokenEnvVar, token, EnvironmentVariableTarget.User);
        }
    }

    // Resolve a presented bearer to the client it authenticates, or null when it
    // matches none. A blank bearer is rejected outright. Every candidate is compared
    // in constant time: the length pre-check short-circuits an obvious mismatch, but
    // two equal-length values still go through FixedTimeEquals so timing never
    // betrays how far the comparison got. The presented token is never logged.
    public McpClientProfile? Authenticate(string? bearer)
    {
        if (string.IsNullOrEmpty(bearer))
            return null;

        // A benign race: two first requests may both build the snapshot, and the
        // last write wins — both read the same vault, so both are right.
        var snapshot = _snapshot ??= LoadSnapshot();

        byte[] presented = Encoding.UTF8.GetBytes(bearer);
        foreach (var (client, expected) in snapshot)
        {
            if (presented.Length == expected.Length &&
                CryptographicOperations.FixedTimeEquals(presented, expected))
            {
                return client;
            }
        }

        return null;
    }

    private IReadOnlyList<(McpClientProfile Client, byte[] Token)> LoadSnapshot()
    {
        var entries = new List<(McpClientProfile, byte[])>(_clients.Count);
        foreach (var client in _clients)
        {
            if (_vault.TryGet(client.TokenSecretName, out string? token) && !string.IsNullOrEmpty(token))
                entries.Add((client, Encoding.UTF8.GetBytes(token)));
        }
        return entries;
    }

    private static void RequireUnique(IEnumerable<string> values, string label)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in values)
            if (!seen.Add(value))
                throw new ArgumentException($"Duplicate MCP {label}: {value}.", nameof(values));
    }

    private static string Mint()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
