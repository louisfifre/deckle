using System.Globalization;

namespace Deckle.Atelier;

// How a size and a date read in the Atelier, as the boards write them:
// "6 Ko", "412 Ko", "2,4 Mo", "1,3 Go", "31/12/2023". Kilobytes are whole and
// rounded up, as File Explorer rounds them (a 10-byte file is "1 Ko", never
// "0 Ko"); larger units carry one decimal. The unit and the number are joined
// by a no-break space so a narrow column never splits them.
public static class EntryFormat
{
    private const char NoBreakSpace = ' ';
    private const double Kilo = 1024;

    public static string Size(long bytes, CultureInfo culture)
    {
        if (bytes <= 0) return $"0{NoBreakSpace}{Unit("Ko", culture)}";
        double kb = bytes / Kilo;
        if (kb < Kilo) return $"{Math.Ceiling(kb).ToString("0", culture)}{NoBreakSpace}{Unit("Ko", culture)}";
        double mb = kb / Kilo;
        if (mb < Kilo) return $"{mb.ToString("0.#", culture)}{NoBreakSpace}{Unit("Mo", culture)}";
        double gb = mb / Kilo;
        if (gb < Kilo) return $"{gb.ToString("0.#", culture)}{NoBreakSpace}{Unit("Go", culture)}";
        return $"{(gb / Kilo).ToString("0.#", culture)}{NoBreakSpace}{Unit("To", culture)}";
    }

    public static string Date(DateTime local, CultureInfo culture) =>
        local.ToString(culture.DateTimeFormat.ShortDatePattern, culture);

    // The extension a type is named by when nothing more is known of it, as
    // File Explorer writes it in "Fichier PDF": upper case, without the dot.
    // Null for a name that has none, which File Explorer calls "Fichier".
    public static string? TypeExtension(string name)
    {
        string extension = Path.GetExtension(name).TrimStart('.');
        return extension.Length == 0 ? null : extension.ToUpperInvariant();
    }

    // The line under a name in the large rows (board 12.16): type, size and
    // date modified, in that order, as File Explorer's Content view lists
    // them. A folder has no size of its own, so its line skips it rather than
    // show an empty place between two separators.
    public static string Summary(string type, long? bytes, DateTime modified, string separator, CultureInfo culture)
    {
        string date = Date(modified, culture);
        return bytes is long size
            ? string.Join(separator, type, Size(size, culture), date)
            : string.Join(separator, type, date);
    }

    // French writes octets (Ko, Mo); every other culture gets the SI letters.
    private static string Unit(string french, CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "fr" ? french : french.Replace('o', 'B');
}
