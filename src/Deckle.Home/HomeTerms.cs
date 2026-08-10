using System.Reflection;
using System.Text.Json;

namespace Deckle.Home;

// Every Anytype-visible label — type names, property names, closed-vocabulary
// options — lives in one embedded terms file per language, the pattern shipped
// by Deckle.Travel. The schema names the structure with stable English keys;
// this file names the words. Adding a language is a file addition
// (Terms/terms.<lang>.json); French is the only language shipped today.
internal sealed class HomeTerms
{
    private const string DefaultLanguage = "fr";

    public static readonly HomeTerms Current = Load(DefaultLanguage);

    private readonly Dto _dto;
    private readonly string _language;

    private HomeTerms(Dto dto, string language)
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

    internal static HomeTerms Load(string language)
    {
        string resource = $"Deckle.Home.Terms.terms.{language}.json";
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream is null)
            throw new InvalidOperationException(
                $"Fichier de termes Home introuvable pour la langue « {language} » ({resource}).");

        Dto? dto = JsonSerializer.Deserialize<Dto>(stream);
        return dto is null
            ? throw new InvalidOperationException($"Fichier de termes Home vide pour « {language} ».")
            : new HomeTerms(dto, language);
    }

    private InvalidOperationException Missing(string path) =>
        new($"Terme Home manquant « {path} » dans la langue « {_language} ».");

    internal sealed record TypeDto(string? name, string? plural);

    internal sealed record Dto(
        string? language,
        Dictionary<string, TypeDto>? types,
        Dictionary<string, string>? properties,
        Dictionary<string, Dictionary<string, string>>? options);
}
