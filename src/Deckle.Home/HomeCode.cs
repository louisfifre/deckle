using System.Text.RegularExpressions;

namespace Deckle.Home;

public readonly record struct HomeElementCode(
    string Value,
    string Room,
    string Category,
    int Sequence)
{
    private static readonly Regex Pattern = new(
        "^(?<room>[A-Z]{2})-(?<category>PS|LR|RJ|RB|RT|DS|DR|DX|DE|P|L|C|V|A)(?<sequence>[0-9]{2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static HomeElementCode Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Le code d’élément ne peut pas être vide.", nameof(value));

        value = value.Trim().ToUpperInvariant();
        Match match = Pattern.Match(value);
        if (!match.Success || !int.TryParse(match.Groups["sequence"].Value, out int sequence) || sequence == 0)
        {
            throw new ArgumentException(
                "Code d’élément invalide. Forme attendue : PIÈCE-CAT[SUB]NN, avec deux lettres de pièce et un numéro de 01 à 99.",
                nameof(value));
        }

        return new HomeElementCode(
            value,
            match.Groups["room"].Value,
            match.Groups["category"].Value,
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

public static class HomeCategories
{
    private static readonly IReadOnlyDictionary<string, string> TypeByCategory =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["P"] = HomeSchema.Types.Outlet,
            ["PS"] = HomeSchema.Types.Outlet,
            ["L"] = HomeSchema.Types.Lighting,
            ["LR"] = HomeSchema.Types.Lighting,
            ["C"] = HomeSchema.Types.Control,
            ["V"] = HomeSchema.Types.Opening,
            ["A"] = HomeSchema.Types.Appliance,
            ["RJ"] = HomeSchema.Types.Network,
            ["RB"] = HomeSchema.Types.Network,
            ["RT"] = HomeSchema.Types.Network,
            ["DS"] = HomeSchema.Types.Sensor,
            ["DR"] = HomeSchema.Types.Relay,
            ["DX"] = HomeSchema.Types.Panel,
            ["DE"] = HomeSchema.Types.Node,
        };

    public static IReadOnlyCollection<string> All => TypeByCategory.Keys.ToArray();

    public static string TypeFor(string category)
    {
        if (TypeByCategory.TryGetValue(category.Trim().ToUpperInvariant(), out string? type))
            return type;

        throw new ArgumentException(
            $"Catégorie inconnue « {category} ». Catégories admises : {string.Join(", ", All)}.",
            nameof(category));
    }
}
