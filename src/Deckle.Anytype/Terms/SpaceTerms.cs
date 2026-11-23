using System.Reflection;
using System.Text.Json;

namespace Deckle.Anytype;

// The words of a space, one embedded file per language: type names, property
// names and option names, each under the stable English key the code sends on
// the wire. The code names the structure; a terms file names the words, so a
// space speaks another language by adding a translated file, with no code.
//
// One display language names what Deckle writes and shows. Reading what a
// person or a model typed accepts the words of every shipped language, so an
// English name works on a French space and the reverse.
//
// A file may also hold `texts`, the free text of a space plan keyed by its
// English text; PlanLocalizer reads it, this class does not.
public sealed class SpaceTerms
{
    private readonly Dto _dto;

    private SpaceTerms(Dto dto, string language)
    {
        _dto = dto;
        Language = language;
    }

    public string Language { get; }

    public string TypeName(string typeKey) =>
        _dto.types is not null
        && _dto.types.TryGetValue(typeKey, out TypeDto? type)
        && !string.IsNullOrWhiteSpace(type?.name)
            ? type.name
            : throw Missing($"types.{typeKey}.name");

    public string TypePluralName(string typeKey) =>
        _dto.types is not null
        && _dto.types.TryGetValue(typeKey, out TypeDto? type)
        && !string.IsNullOrWhiteSpace(type?.plural)
            ? type.plural
            : throw Missing($"types.{typeKey}.plural");

    public string PropertyName(string propertyKey) =>
        TryPropertyName(propertyKey) ?? throw Missing($"properties.{propertyKey}");

    public string? TryPropertyName(string propertyKey) =>
        _dto.properties is not null
        && _dto.properties.TryGetValue(propertyKey, out string? name)
        && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;

    public string OptionName(string propertyKey, string optionKey) =>
        TryOptionName(propertyKey, optionKey) ?? throw Missing($"options.{propertyKey}.{optionKey}");

    public string? TryOptionName(string propertyKey, string optionKey) =>
        _dto.options is not null
        && _dto.options.TryGetValue(propertyKey, out Dictionary<string, string>? options)
        && options.TryGetValue(optionKey, out string? name)
        && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;

    // The keys this file names, so a test can hold every language to the same set.
    public IEnumerable<string> TypeKeys => _dto.types?.Keys ?? Enumerable.Empty<string>();
    public IEnumerable<string> PropertyKeys => _dto.properties?.Keys ?? Enumerable.Empty<string>();
    public IEnumerable<(string Property, string Option)> OptionKeys =>
        (_dto.options ?? []).SelectMany(p => p.Value.Keys.Select(o => (p.Key, o)));

    // Resource names follow <prefix>.<language>.json, the prefix being the
    // embedded folder and file stem (Deckle.Anytype.Terms.pm).
    public static SpaceTerms Load(Assembly assembly, string resourcePrefix, string language)
    {
        string resource = $"{resourcePrefix}.{language}.json";
        using Stream? stream = assembly.GetManifestResourceStream(resource);
        if (stream is null)
            throw new InvalidOperationException(
                $"Fichier de termes introuvable pour la langue « {language} » ({resource}).");

        Dto? dto = JsonSerializer.Deserialize<Dto>(stream);
        return dto is null
            ? throw new InvalidOperationException($"Fichier de termes vide pour « {language} » ({resource}).")
            : new SpaceTerms(dto, language);
    }

    // Every language shipped beside the prefix, the display language first.
    public static IReadOnlyList<SpaceTerms> LoadAll(Assembly assembly, string resourcePrefix, string displayLanguage)
    {
        string head = resourcePrefix + ".";
        IEnumerable<string> others = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(head, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => n[head.Length..^".json".Length])
            .Where(l => l.Length > 0 && !l.Contains('.') && l != displayLanguage)
            .Order(StringComparer.Ordinal);

        return [Load(assembly, resourcePrefix, displayLanguage), .. others.Select(l => Load(assembly, resourcePrefix, l))];
    }

    private InvalidOperationException Missing(string path) =>
        new($"Terme manquant « {path} » dans la langue « {Language} ».");

    internal sealed record TypeDto(string? name, string? plural);

    internal sealed record Dto(
        string? language,
        Dictionary<string, TypeDto>? types,
        Dictionary<string, string>? properties,
        Dictionary<string, Dictionary<string, string>>? options);
}
