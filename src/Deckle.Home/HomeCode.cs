using System.Text.RegularExpressions;

namespace Deckle.Home;

public readonly record struct HomeElementCode(
    string Value,
    string Room,
    string Category,
    int Sequence)
{
    private static readonly Regex Pattern = new(
        "^(?<room>[A-Z]{2})-(?<category>[A-Z]{2})(?<sequence>[0-9]{2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static HomeElementCode Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Le code de point ne peut pas être vide.", nameof(value));

        value = value.Trim().ToUpperInvariant();
        Match match = Pattern.Match(value);
        if (!match.Success || !int.TryParse(match.Groups["sequence"].Value, out int sequence) || sequence == 0)
        {
            throw new ArgumentException(
                "Code de point invalide. Forme attendue : PIÈCE-CAT[SUB]NN, avec deux lettres de pièce et un numéro de 01 à 99.",
                nameof(value));
        }

        return new HomeElementCode(
            value,
            match.Groups["room"].Value,
            HomeCategories.Validate(match.Groups["category"].Value),
            sequence);
    }

    public static string ValidateRoomCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Le code de pièce ne peut pas être vide.", nameof(value));

        value = value.Trim().ToUpperInvariant();
        if (value.Length != 2 || value.Any(c => !char.IsAsciiLetterUpper(c)))
            throw new ArgumentException("Un code de pièce porte exactement deux lettres ASCII majuscules.", nameof(value));
        return value;
    }
}

// Retained category vocabulary from the accepted target manifest. Some entries
// await the assembly/network review; recognition is not a classification rule.
public static class HomeCategories
{
    private static readonly IReadOnlyList<string> Codes =
        HomeSchema.TargetManifest["properties"]!.AsArray().OfType<System.Text.Json.Nodes.JsonObject>()
            .Single(property => property["key"]!.GetValue<string>() == "category")["tags"]!
            .AsArray().OfType<System.Text.Json.Nodes.JsonObject>()
            .Select(tag => tag["key"]!.GetValue<string>().ToUpperInvariant()).ToArray();

    public static IReadOnlyCollection<string> All => Codes;

    public static string Validate(string category)
    {
        string normalized = category.Trim().ToUpperInvariant();
        if (Codes.Contains(normalized)) return normalized;

        throw new ArgumentException(
            $"Catégorie inconnue « {category} ». Catégories admises : {string.Join(", ", Codes)}.",
            nameof(category));
    }

    public static string OptionKey(string category) => Validate(category).ToLowerInvariant();
}
