using System.Globalization;
using Deckle.Catalog;

namespace Deckle.Atelier.Views;

// The Atelier's strings read from code, out of this library's own catalog
// (Strings/en-US/Resources.resw). XAML reads the same catalog through x:Uid.
internal static class AtelierText
{
    private const string Library = "Deckle.Atelier.Views";

    // The interface is French; sizes and dates follow it.
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("fr-FR");

    public static string Get(string key) => Loc.GetFrom(Library, key);

    public static string Format(string key, params object?[] args) =>
        string.Format(Culture, Get(key), args);
}
