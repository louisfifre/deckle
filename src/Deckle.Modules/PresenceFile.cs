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
// A missing file reads as null, "no choice recorded", which callers interpret
// as everything present (older installs, dev builds). A corrupt file also reads
// as null, after a warning: falling back to all-present beats making modules
// vanish on a bad byte.
public static class PresenceFile
{
    private const int CurrentVersion = 1;

    private sealed record Payload(int Version, List<string> Present);

    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

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

    // Write-then-rename, so a crash never leaves a half-written choice.
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
