using System.IO;
using System.Text.Json;
using Deckle.Core;

namespace Deckle.Anytype;

// Local Anytype API credentials, loaded from disk. The api_key is a bearer
// token paired to the running Anytype Desktop instance — it never leaves this
// machine and is never logged (see DeckleAnytypeSource: no event carries key
// material). Provisioning the file (the auth challenge handshake) is out of
// scope here; this type only reads an existing credentials.json.
public sealed record AnytypeCredentials(string ApiUrl, string ApiVersion, string ApiKey, string SpaceId)
{
    private const string ModuleId = "anytype";
    private const string FileName = "credentials.json";

    // JSON keys are snake_case to match the on-disk file written by the
    // provisioning step (and the vendor reference shape).
    private sealed record Dto(string? api_url, string? api_version, string? api_key, string? space_id);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Loads %LOCALAPPDATA%\Deckle\modules\anytype\credentials.json. Throws
    // InvalidOperationException with a remediation message when the file is
    // absent or any field is blank — the caller surfaces it to the user; a
    // half-populated credentials file is a configuration error, not a runtime
    // condition to limp through.
    public static AnytypeCredentials Load()
    {
        string path = Path.Combine(AppPaths.GetModuleDirectory(ModuleId), FileName);

        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Anytype credentials not found at {path}. Provision them by pairing with Anytype Desktop (auth challenge) and writing api_url, api_version, api_key and space_id.");

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

        if (dto is null ||
            string.IsNullOrWhiteSpace(dto.api_url) ||
            string.IsNullOrWhiteSpace(dto.api_version) ||
            string.IsNullOrWhiteSpace(dto.api_key) ||
            string.IsNullOrWhiteSpace(dto.space_id))
        {
            throw new InvalidOperationException(
                $"Anytype credentials at {path} are incomplete. Expected non-empty api_url, api_version, api_key and space_id.");
        }

        return new AnytypeCredentials(dto.api_url, dto.api_version, dto.api_key, dto.space_id);
    }
}
