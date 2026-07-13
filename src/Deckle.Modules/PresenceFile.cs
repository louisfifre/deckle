using System.Text.Json;

namespace Deckle.Modules;

// ── PresenceFile ──────────────────────────────────────────────────────────────
//
// Reads and writes the presence choice on disk — the one JSON file that says
// which modules the user chose to have installed. Path-parameterized and free of
// any static state so tests exercise it against a temp folder, and so the
// installer companion can reuse it against a live install from outside the app.
//
// Shape: { "version": 1, "present": ["ambient", "transcription"] }.
//
// A missing file reads as null — "no choice recorded", which callers interpret
// as everything present (the state of every install that predates the presence
// model, and of every dev build). A corrupt file also reads as null, after a
// warning: falling back to all-present degrades to today's behaviour instead of
// making modules vanish on a bad byte.
public static class PresenceFile
{
    private const int CurrentVersion = 1;

    private sealed record Payload(int Version, List<string> Present);

    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // The recorded choice, or null when no valid choice is on disk.
    public static IReadOnlySet<string>? LoadFrom(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(File.ReadAllText(path), _options);
            if (payload?.Present is null) return null;
            return new HashSet<string>(payload.Present, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DeckleModulesSource.Log.PresenceLoadFailed();
            DeckleModulesSource.Log.PresenceLoadFailedDetail(ex.GetType().Name, ex.Message, path);
            return null;
        }
    }

    // Records the choice atomically (write-then-rename), creating the parent
    // folder on first save.
    public static void SaveTo(string path, IReadOnlyCollection<string> present)
    {
        string json = JsonSerializer.Serialize(
            new Payload(CurrentVersion, present.OrderBy(id => id, StringComparer.Ordinal).ToList()),
            _options);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
