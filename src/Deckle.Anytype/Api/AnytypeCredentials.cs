using System.IO;
using System.Text.Json;
using Deckle.Core;
using Deckle.Security;

namespace Deckle.Anytype;

// Local Anytype API credentials. Two provisioning worlds resolve here, and
// where the bearer lives decides which backend the client talks to:
//
//   • Headless — the Deckle vault holds the bot API key (minted against the
//     headless serve, JOURNAL 2026-07-01). That key only exists against the
//     headless backend, so its presence pins the base URL to the fixed 31012
//     listener; the file's api_url is not consulted.
//   • Desktop (legacy) — no vault entry: the file's api_key + api_url (the
//     Desktop challenge pairing) keep working, until the space cutover
//     retires them.
//
// The non-secret coordinates (api_version, space_id) stay in the module file
// in both worlds; only the secret moved to the vault. No key material is ever
// logged (see DeckleAnytypeSource: no event carries it).
public sealed record AnytypeCredentials(string ApiUrl, string ApiVersion, string ApiKey, string SpaceId)
{
    private const string ModuleId = "anytype";
    private const string FileName = "credentials.json";

    // The vault name of the bot API key. The provisioning act (the wizard,
    // eventually) writes it; resolution here reads it.
    public const string ApiKeySecretName = "anytype-api-key";

    // JSON keys are snake_case to match the on-disk file written by the
    // provisioning step (and the vendor reference shape).
    internal sealed record Dto(string? api_url, string? api_version, string? api_key, string? space_id);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Loads %LOCALAPPDATA%\Deckle\modules\anytype\credentials.json plus the
    // default vault. Throws InvalidOperationException with a remediation
    // message when the file is absent, a coordinate is blank, or no bearer
    // exists in either home — a half-provisioned machine is a configuration
    // error, not a runtime condition to limp through. An unreadable vault
    // (SecretVaultException) propagates for the same reason.
    public static AnytypeCredentials Load() => Load(SecretVault.CreateDefault());

    public static AnytypeCredentials Load(ISecretVault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        string path = Path.Combine(AppPaths.GetModuleDirectory(ModuleId), FileName);

        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Anytype credentials not found at {path}. Expected api_version and space_id (plus api_url and api_key for the legacy Desktop pairing).");

        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Anytype credentials at {path} are not valid JSON: {ex.Message}");
        }

        vault.TryGet(ApiKeySecretName, out string? vaultApiKey);
        return Resolve(dto, vaultApiKey, path);
    }

    // The resolution contract, pure so it pins under test: vault bearer →
    // headless profile on the fixed listener; file bearer alone → legacy
    // Desktop profile; neither → a remediation error.
    internal static AnytypeCredentials Resolve(Dto? dto, string? vaultApiKey, string path)
    {
        if (dto is null ||
            string.IsNullOrWhiteSpace(dto.api_version) ||
            string.IsNullOrWhiteSpace(dto.space_id))
        {
            throw new InvalidOperationException(
                $"Anytype credentials at {path} are incomplete. Expected non-empty api_version and space_id.");
        }

        if (!string.IsNullOrWhiteSpace(vaultApiKey))
        {
            DeckleAnytypeSource.Log.CredentialsResolved("headless");
            return new AnytypeCredentials(
                BackendHealthProbe.DefaultBaseUrl, dto.api_version, vaultApiKey, dto.space_id);
        }

        if (!string.IsNullOrWhiteSpace(dto.api_key) && !string.IsNullOrWhiteSpace(dto.api_url))
        {
            DeckleAnytypeSource.Log.CredentialsResolved("desktop");
            return new AnytypeCredentials(dto.api_url, dto.api_version, dto.api_key, dto.space_id);
        }

        throw new InvalidOperationException(
            $"No Anytype API key found: the vault holds no '{ApiKeySecretName}' secret and {path} carries no api_key/api_url pair. Provision the headless backend (mint an API key into the vault) or the legacy Desktop pairing.");
    }
}
