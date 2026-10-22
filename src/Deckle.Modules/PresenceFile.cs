using System.Text.Json;

namespace Deckle.Modules;

// ── PresenceFile ──────────────────────────────────────────────────────────────
//
// Reads and writes the presence choice on disk — the one JSON file that says
// which modules the user chose to have installed. Path-parameterized and free of
// any static state so tests exercise it against a temp folder, and so the
// installer companion can reuse it against a live install from outside the app.
//
// Shape: { "version": 2, "present": ["ambient", "transcription"],
//          "known": ["ambient", "rewrite", "transcription"] }.
// "known" lists what the catalogue offered when the choice was made, so a module
// added later reads as never offered, not declined (see PresenceChoice).
//
// A missing file reads as null, "no choice recorded", which callers interpret
// as everything present (older installs, dev builds). A corrupt file also reads
// as null, after a warning: falling back to all-present beats making modules
// vanish on a bad byte.
public static class PresenceFile
{
    private const int CurrentVersion = 2;

    private sealed record Payload(int Version, List<string> Present, List<string>? Known = null);

    // The catalogue a version-1 file was written against. That shape had no
    // "known", and these six were the whole offer then; a module added since is
    // not in this list, so a file written before it existed keeps it present.
    // A fixed list, never read from the registry: it is a fact about old files,
    // not the current catalogue. It names the ids through ModuleIds, whose
    // values are the strings those files hold.
    private static readonly string[] _legacyKnown =
    [
        ModuleIds.Transcription,
        ModuleIds.Rewrite,
        ModuleIds.Autocorrect,
        ModuleIds.Ambient,
        ModuleIds.Trackpad,
        ModuleIds.Anytype,
    ];

    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static PresenceChoice? LoadFrom(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(File.ReadAllText(path), _options);
            if (payload?.Present is null)
            {
                DeckleModulesSource.Log.PresenceLoadFailed(
                    DeckleModulesSource.PresenceLoadFailureReasons.PresentListMissing);
                return null;
            }

            // Keyed on the field being absent, not on the version number: a file
            // that lacks "known" cannot say what was offered, whatever it claims.
            return new PresenceChoice(
                new HashSet<string>(payload.Present, StringComparer.Ordinal),
                new HashSet<string>(payload.Known ?? [.. _legacyKnown], StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DeckleModulesSource.Log.PresenceLoadFailed(ex.GetType().Name);
            return null;
        }
    }

    // Write-then-rename, so a crash never leaves a half-written choice. Always
    // the current shape: a version-1 file is read as it stands and replaced at
    // the next write, which a change in the module selector and every install
    // or update make.
    public static void SaveTo(
        string path, IReadOnlyCollection<string> present, IReadOnlyCollection<string> known)
    {
        string json = JsonSerializer.Serialize(
            new Payload(
                CurrentVersion,
                present.OrderBy(id => id, StringComparer.Ordinal).ToList(),
                known.OrderBy(id => id, StringComparer.Ordinal).ToList()),
            _options);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
