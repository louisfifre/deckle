using System.Globalization;

namespace Deckle.Atelier;

// How capture data reads in the details panel, as the boards write it (12.4):
// "dim. 14 juin 2026, 15:42", "Kestrel K2", "35 mm f/1,8",
// "1/250 s · f/2,8 · ISO 400", "45,7640° N, 4,8357° E". A value the file does
// not hold returns null, so the panel leaves its row out rather than showing
// an empty one.
public static class CaptureDataFormat
{
    // Between a number and its unit, so a narrow panel never splits them.
    private const char NoBreakSpace = ' ';
    private const string Separator = " · ";

    public static string? Taken(CaptureData data, CultureInfo culture) =>
        data.Taken is { } t ? t.ToString("ddd d MMMM yyyy, HH:mm", culture) : null;

    // The make is left out when the model already names it, as cameras often
    // write "NIKON CORPORATION" then "NIKON Z 6".
    public static string? Camera(CaptureData data)
    {
        string? make = data.CameraMake;
        string? model = data.CameraModel;
        if (model is null) return make;
        if (make is null) return model;
        string firstWord = make.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return model.StartsWith(firstWord, StringComparison.OrdinalIgnoreCase) ? model : $"{make} {model}";
    }

    // The lens name when the file holds one, else the focal length alone.
    public static string? Lens(CaptureData data, CultureInfo culture) =>
        data.Lens ?? (data.FocalLengthMm is { } f ? $"{f.ToString("0.#", culture)}{NoBreakSpace}mm" : null);

    public static string? Exposure(CaptureData data, CultureInfo culture)
    {
        var parts = new List<string>(3);
        if (data.Exposure is { } e) parts.Add(Shutter(e, culture));
        if (data.FNumber is { } n) parts.Add($"f/{n.ToString("0.#", culture)}");
        if (data.Iso is { } iso) parts.Add($"ISO{NoBreakSpace}{iso.ToString(culture)}");
        return parts.Count == 0 ? null : string.Join(Separator, parts);
    }

    public static string? Place(CaptureData data, CultureInfo culture)
    {
        if (data.Place is not { } p) return null;
        string lat = $"{Math.Abs(p.Latitude).ToString("0.0000", culture)}°{NoBreakSpace}{(p.Latitude >= 0 ? "N" : "S")}";
        string lon = $"{Math.Abs(p.Longitude).ToString("0.0000", culture)}°{NoBreakSpace}{(p.Longitude >= 0 ? "E" : West(culture))}";
        return $"{lat}, {lon}";
    }

    // French writes O for ouest; every other culture gets W.
    private static string West(CultureInfo culture) => culture.TwoLetterISOLanguageName == "fr" ? "O" : "W";

    // Under a second, the fraction photographers read ("1/250 s"); from a
    // second up, the seconds ("2 s", "1,5 s").
    private static string Shutter(ExposureTime e, CultureInfo culture)
    {
        double seconds = e.Seconds;
        if (seconds >= 1) return $"{seconds.ToString("0.#", culture)}{NoBreakSpace}s";
        long denominator = (long)Math.Round(1 / seconds);
        return $"1/{denominator.ToString(culture)}{NoBreakSpace}s";
    }
}
