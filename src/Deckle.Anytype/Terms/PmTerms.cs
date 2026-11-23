namespace Deckle.Anytype;

// The words of the project-management space (Terms/pm.<language>.json), read
// by DevSpace. French is the display language: the person's spaces are French,
// and Deckle names properties and options the way the space shows them. The
// other shipped languages only widen what a typed name may be.
public static class PmTerms
{
    public const string DisplayLanguage = "fr";

    public static IReadOnlyList<SpaceTerms> Languages { get; } =
        SpaceTerms.LoadAll(typeof(PmTerms).Assembly, "Deckle.Anytype.Terms.pm", DisplayLanguage);

    public static SpaceTerms Display => Languages[0];

    // True when the typed text is the name of this property in a shipped language.
    public static bool IsPropertyName(string propertyKey, string text) =>
        Languages.Any(t => string.Equals(t.TryPropertyName(propertyKey), text.Trim(), StringComparison.OrdinalIgnoreCase));

    // True when the typed text is the name of this option in a shipped language.
    public static bool IsOptionName(string propertyKey, string optionKey, string text) =>
        Languages.Any(t => string.Equals(t.TryOptionName(propertyKey, optionKey), text.Trim(), StringComparison.OrdinalIgnoreCase));

    // The option's names in every shipped language, the display one first.
    public static IEnumerable<string> OptionNames(string propertyKey, string optionKey) =>
        Languages.Select(t => t.TryOptionName(propertyKey, optionKey)).OfType<string>().Distinct(StringComparer.Ordinal);
}
