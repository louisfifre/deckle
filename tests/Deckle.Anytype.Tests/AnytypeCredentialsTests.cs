using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Pins the credentials resolution contract: where the bearer lives decides
// which backend the client talks to. The vault key must win over a file key
// and must force the headless listener — a vault bearer only exists against
// the headless backend, so pairing it with the file's Desktop URL would be a
// guaranteed 401.
[Trait("Category", "unit")]
public class AnytypeCredentialsTests
{
    private const string CredentialsPath = "X:\\credentials.json";

    private static AnytypeCredentials.Dto Dto(
        string? apiUrl = "http://localhost:31009",
        string? apiKey = "file-key",
        string? apiVersion = "2025-11-08",
        string? spaceId = "space-1") => new(apiUrl, apiVersion, apiKey, spaceId);

    [Fact]
    public void VaultKeyResolvesHeadlessOnTheFixedListener()
    {
        var creds = AnytypeCredentials.Resolve(Dto(), "vault-key", CredentialsPath);

        Assert.Equal("vault-key", creds.ApiKey);
        // The value the code uses: the probe's frozen headless base URL, not a
        // re-typed literal.
        Assert.Equal(BackendHealthProbe.DefaultBaseUrl, creds.ApiUrl);
        Assert.Equal("2025-11-08", creds.ApiVersion);
        Assert.Equal("space-1", creds.SpaceId);
    }

    [Fact]
    public void VaultKeyWinsOverAFileKey()
    {
        var creds = AnytypeCredentials.Resolve(Dto(apiKey: "file-key"), "vault-key", CredentialsPath);

        Assert.Equal("vault-key", creds.ApiKey);
        Assert.Equal(BackendHealthProbe.DefaultBaseUrl, creds.ApiUrl);
    }

    [Fact]
    public void FileKeyAloneResolvesTheLegacyDesktopPairing()
    {
        var creds = AnytypeCredentials.Resolve(Dto(), vaultApiKey: null, CredentialsPath);

        Assert.Equal("file-key", creds.ApiKey);
        Assert.Equal("http://localhost:31009", creds.ApiUrl);
    }

    [Fact]
    public void NoKeyAnywhereIsARemediationError()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => AnytypeCredentials.Resolve(Dto(apiKey: null), vaultApiKey: null, CredentialsPath));

        // The message must name both homes so the user knows where to provision.
        Assert.Contains(AnytypeCredentials.ApiKeySecretName, ex.Message);
        Assert.Contains(CredentialsPath, ex.Message);
    }

    [Fact]
    public void MissingCoordinatesThrowRegardlessOfTheBearer()
    {
        Assert.Throws<InvalidOperationException>(
            () => AnytypeCredentials.Resolve(Dto(spaceId: null), "vault-key", CredentialsPath));
        Assert.Throws<InvalidOperationException>(
            () => AnytypeCredentials.Resolve(null, "vault-key", CredentialsPath));
    }
}
