using System.Globalization;
using System.Text.Json;

namespace Deckle.Browser;

/// <summary>What the media card shows of a page that plays: the title and the
/// artist (the page's media session, else its own title), the position and the
/// duration in seconds (zero for a stream with none), whether it is paused,
/// whether a picture can be shown (a video) and whether the site has a button
/// for the next item.</summary>
public sealed record MediaReading(string Title, string Artist, double Position, double Duration, bool Paused, bool HasVideo, bool HasNext)
{
    /// <summary>The read of the page's media, as a script returns it: the
    /// JSON text of a string holding an object, or the object itself. Null
    /// when there is nothing to read (null, not JSON, not an object).</summary>
    public static MediaReading? Parse(string? scriptResult)
    {
        if (string.IsNullOrWhiteSpace(scriptResult)) return null;
        try
        {
            using JsonDocument outer = JsonDocument.Parse(scriptResult);
            JsonElement root = outer.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                using JsonDocument inner = JsonDocument.Parse(root.GetString() ?? "");
                return From(inner.RootElement);
            }
            return From(root);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static MediaReading? From(JsonElement o)
    {
        if (o.ValueKind != JsonValueKind.Object) return null;
        return new MediaReading(
            Text(o, "title"), Text(o, "artist"),
            Number(o, "position"), Number(o, "duration"),
            Flag(o, "paused", true), Flag(o, "hasVideo", false), Flag(o, "hasNext", false));
    }

    private static string Text(JsonElement o, string name) =>
        o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double Number(JsonElement o, string name) =>
        o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number
        && v.TryGetDouble(out double d) && double.IsFinite(d) && d > 0 ? d : 0;

    private static bool Flag(JsonElement o, string name, bool fallback) =>
        o.TryGetProperty(name, out JsonElement v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : fallback;

    /// <summary>How far in, from 0 to 1; zero when the duration is not known.</summary>
    public double Progress => Duration > 0 ? Math.Clamp(Position / Duration, 0, 1) : 0;

    /// <summary>A time as a card shows it: m:ss, or h:mm:ss from an hour.</summary>
    public static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? Math.Floor(seconds) : 0));
        return t.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{t.Minutes}:{t.Seconds:00}");
    }
}
