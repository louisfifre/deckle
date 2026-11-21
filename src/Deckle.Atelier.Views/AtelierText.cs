using System.Globalization;
using Deckle.Localization;

namespace Deckle.Atelier.Views;

// The Atelier's strings read from code, out of this library's own catalog
// (Strings/en-US/Resources.resw). XAML reads the same catalog through x:Uid.
internal static class AtelierText
{
    private const string Library = "Deckle.Atelier.Views";

    // Sizes and dates follow the user's regional format, as Loc.Format does
    // for the rest of Deckle and as File Explorer does.
    public static CultureInfo Culture => CultureInfo.CurrentCulture;

    public static string Get(string key) => Loc.GetFrom(Library, key);

    public static string Format(string key, params object?[] args) =>
        string.Format(Culture, Get(key), args);
}
