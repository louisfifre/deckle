using System.Globalization;

namespace Deckle.Atelier;

// How a size and a date read in the Atelier, as the boards write them:
// "6 KB", "412 KB", "2.4 MB", "1.3 GB", "12/31/2023" (in French: "6 Ko",
// "2,4 Mo", "31/12/2023"; the culture follows the user's region). Kilobytes are
// whole and rounded up, as File Explorer rounds them (a 10-byte file is "1 KB",
// never "0 KB"); larger units carry one decimal. The unit and the number are joined
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
    // File Explorer writes it in "PDF File": upper case, without the dot.
    // Null for a name that has none, which File Explorer calls "File".
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

    // A count in its sentence: "1 file", "2 files", "1,234 files" (French: "1 fichier", "1 234 fichiers").
    // French puts 0 and 1 in the singular and English 1 alone, so the rule
    // follows the culture; the number takes its group separator.
    public static string Count(int count, string oneFormat, string manyFormat, CultureInfo culture)
    {
        bool singular = culture.TwoLetterISOLanguageName == "fr" ? count is 0 or 1 : count == 1;
        return string.Format(culture, singular ? oneFormat : manyFormat, count.ToString("N0", culture));
    }

    // French writes octets (Ko, Mo); every other culture gets the SI letters.
    private static string Unit(string french, CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "fr" ? french : french.Replace('o', 'B');
}
