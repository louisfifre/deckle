using System.Text.Json;
using Deckle.Core;

namespace Deckle.Anytype;

// Named space coordinates for guarded cross-space tools. Domain surfaces keep
// their own fixed space, while schema administration names an allow-listed alias
// such as "dev" or "home" and never accepts a raw space id from a model.
public sealed class AnytypeSpaceAliases
{
    private const string ModuleId = "anytype";
    private const string FileName = "spaces.json";

    private readonly IReadOnlyDictionary<string, string> _aliases;

    public AnytypeSpaceAliases(IReadOnlyDictionary<string, string> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        _aliases = new Dictionary<string, string>(aliases, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string> All => _aliases;

    public static AnytypeSpaceAliases Load(string devSpaceId) =>
        Load(devSpaceId, Path.Combine(AppPaths.GetModuleDirectory(ModuleId), FileName));

    internal static AnytypeSpaceAliases Load(string devSpaceId, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devSpaceId);

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dev"] = devSpaceId,
        };

        if (File.Exists(path))
        {
            Dto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<Dto>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Anytype space aliases at {path} are not valid JSON: {ex.Message}");
            }

            if (dto?.aliases is not null)
                foreach (var (alias, spaceId) in dto.aliases)
                    if (!string.IsNullOrWhiteSpace(alias) && !string.IsNullOrWhiteSpace(spaceId))
                    {
                        string normalizedAlias = alias.Trim();
                        string normalizedSpaceId = spaceId.Trim();
                        if (string.Equals(normalizedAlias, "dev", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.Equals(normalizedSpaceId, devSpaceId, StringComparison.Ordinal))
                                throw new InvalidOperationException(
                                    "L'alias Anytype « dev » est réservé au space des credentials actifs.");
                            continue;
                        }

                        aliases[normalizedAlias] = normalizedSpaceId;
                    }
        }

        return new AnytypeSpaceAliases(aliases);
    }

    public string Resolve(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("L'alias d'espace ne peut pas être vide.", nameof(alias));

        if (_aliases.TryGetValue(alias.Trim(), out string? spaceId))
            return spaceId;

        string known = string.Join(", ", _aliases.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        throw new InvalidOperationException(
            $"Alias d'espace inconnu « {alias} ». Alias connus : {known}.");
    }

    internal sealed record Dto(Dictionary<string, string>? aliases);
}
