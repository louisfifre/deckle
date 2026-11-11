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

    // French writes octets (Ko, Mo); every other culture gets the SI letters.
    private static string Unit(string french, CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "fr" ? french : french.Replace('o', 'B');
}
