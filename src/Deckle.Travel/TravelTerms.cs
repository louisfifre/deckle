using System.Reflection;
using System.Text.Json;

namespace Deckle.Travel;

// Every Anytype-visible label — type names, property names, closed-vocabulary
// options — lives in one embedded terms file per language. The schema names
// the structure with stable English keys; this file names the words. Adding a
// language is a file addition (Terms/terms.<lang>.json), deferred to the
// app-wide language pass; French is the only language shipped today.
internal sealed class TravelTerms
{
    private const string DefaultLanguage = "fr";

    public static readonly TravelTerms Current = Load(DefaultLanguage);

    private readonly Dto _dto;
    private readonly string _language;

    private TravelTerms(Dto dto, string language)
    {
        _dto = dto;
        _language = language;
    }

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
        _dto.properties is not null
        && _dto.properties.TryGetValue(propertyKey, out string? name)
        && !string.IsNullOrWhiteSpace(name)
            ? name
            : throw Missing($"properties.{propertyKey}");

    public string OptionName(string propertyKey, string optionKey) =>
        _dto.options is not null
        && _dto.options.TryGetValue(propertyKey, out Dictionary<string, string>? options)
        && options.TryGetValue(optionKey, out string? name)
        && !string.IsNullOrWhiteSpace(name)
            ? name
            : throw Missing($"options.{propertyKey}.{optionKey}");

    internal static TravelTerms Load(string language)
    {
        string resource = $"Deckle.Travel.Terms.terms.{language}.json";
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream is null)
            throw new InvalidOperationException(
                $"Fichier de termes Travel introuvable pour la langue « {language} » ({resource}).");

        Dto? dto = JsonSerializer.Deserialize<Dto>(stream);
        return dto is null
            ? throw new InvalidOperationException($"Fichier de termes Travel vide pour « {language} ».")
            : new TravelTerms(dto, language);
    }

    private InvalidOperationException Missing(string path) =>
        new($"Terme Travel manquant « {path} » dans la langue « {_language} ».");

    internal sealed record TypeDto(string? name, string? plural);

    internal sealed record Dto(
        string? language,
        Dictionary<string, TypeDto>? types,
        Dictionary<string, string>? properties,
        Dictionary<string, Dictionary<string, string>>? options);
}
