using System.Text.RegularExpressions;

namespace Deckle.Home;

public readonly record struct HomeElementCode(
    string Value,
    string Room,
    string Category,
    int Sequence)
{
    private static readonly Regex Pattern = new(
        "^(?<room>[A-Z]{2})-(?<category>PS|PJ|PF|LR|DS|DR|DX|DE|P|L|C|V|A)(?<sequence>[0-9]{2})$",
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

// The 13 category codes of nomenclature v3 (2026-08-23: the R family
// dissolved into P — a network socket is a socket, PJ RJ45 and PF fibre;
// the bay is a panel; coax and TPL are not inventoried). Since the point
// merge (2026-08-10) every category maps to the single point type: the
// category is the point's nature, carried by the `category` select — no
// longer a type discriminator. The select option key is the category code
// lowercased.
public static class HomeCategories
{
    private static readonly IReadOnlyList<string> Codes =
    ["P", "PS", "PJ", "PF", "L", "LR", "C", "V", "A", "DS", "DR", "DX", "DE"];

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
