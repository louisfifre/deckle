using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Deckle.Core;

namespace Deckle.Home;

// The replay sheet of a recreation rehearsal: every object of a source space,
// already on the manifest keys, in the shape the typed verbs take. It is the
// pivot between the three steps (export, replay, compare), so each can be run,
// read and repeated on its own.
//
// A sheet holds the person's Home data. It lives OUTSIDE the repository, under
// Deckle's local data root, and is written to disk only: this class never
// logs it, never prints it and returns nothing from it but the model the
// caller asked to load. The reports written beside it hold names too and
// follow the same rule.
internal sealed class HomeReplaySheet
{
    // Overrides the default location of a sheet (a full file path).
    public const string PathVariable = "DECKLE_HOME_REPLAY_SHEET";

    private const string ModuleId = "home";
    private const string FolderName = "rehearsal";

    // Relaxed escaping keeps accents readable for the person who opens the
    // file; it is never embedded in HTML, so the relaxed encoder is safe here.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [JsonPropertyName("source_space")]
    public string SourceSpace { get; set; } = "";

    [JsonPropertyName("source_alias")]
    public string? SourceAlias { get; set; }

    [JsonPropertyName("exported_at")]
    public DateTimeOffset ExportedAt { get; set; }

    [JsonPropertyName("objects")]
    public List<HomeReplayObject> Objects { get; set; } = [];

    [JsonPropertyName("report")]
    public HomeReplaySheetReport Report { get; set; } = new();

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        WriteJson(path, this);
    }

    public static HomeReplaySheet Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return JsonSerializer.Deserialize<HomeReplaySheet>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("La feuille de rejeu est vide.");
    }

    // Any report of the rehearsal (replay details, differences, counts) goes
    // through here, so they all share the sheet's encoding and its folder.
    internal static void WriteJson<T>(string path, T value)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    // The sheet path for a rehearsal of `sourceAlias` started at `now`: the
    // variable when set, else the rehearsal folder of the Home module.
    public static string ResolvePath(string sourceAlias, DateTimeOffset now)
    {
        string? overridden = Environment.GetEnvironmentVariable(PathVariable);
        if (!string.IsNullOrWhiteSpace(overridden)) return overridden.Trim();
        return DefaultPath(AppPaths.GetModuleDirectory(ModuleId), sourceAlias, now);
    }

    // <module>\rehearsal\<yyyyMMdd-HHmm>-<alias>.json, local time so the
    // person finds the run by the hour they launched it.
    internal static string DefaultPath(string moduleDirectory, string sourceAlias, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAlias);
        string alias = string.Concat(sourceAlias.Trim().Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
        string stamp = now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        return Path.Combine(moduleDirectory, FolderName, $"{stamp}-{alias}.json");
    }

    // A report beside the sheet: same folder, same stem, its own suffix
    // ("replay" gives <stem>.replay.json).
    public static string Beside(string sheetPath, string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(suffix);
        string directory = Path.GetDirectoryName(Path.GetFullPath(sheetPath)) ?? "";
        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(sheetPath)}.{suffix}.json");
    }
}

// One object of the source, bridged to the manifest keys. Properties carry the
// values as the typed verb takes them: text, number, boolean, date string,
// option NAME (the live option keys are hash-derived and do not exist on
// another space; the verbs resolve a name), and for relations an array of
// selectors, a code for a coded type and a name for the others. References
// keep the source ids behind each relation, so the replay can point at the
// recreated object even when two objects share a name.
internal sealed class HomeReplayObject
{
    [JsonPropertyName("old_id")]
    public string OldId { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    // Anytype's own done box of a task, written back through complete.
    [JsonPropertyName("done")]
    public bool? Done { get; set; }

    [JsonPropertyName("properties")]
    public JsonObject Properties { get; set; } = [];

    [JsonPropertyName("references")]
    public Dictionary<string, List<string>> References { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("collections")]
    public List<string> Collections { get; set; } = [];

    // Old key → raw value of what the bridge does not map: dead keys, a
    // category outside the 22, files, links to objects that are not
    // exported. Kept so nothing is lost silently, never guessed into a field.
    [JsonPropertyName("unmapped")]
    public JsonObject Unmapped { get; set; } = [];

    // The selector a relation uses to name this object: its code when the
    // type is coded, else its name, the first line of its text for an idea.
    [JsonIgnore]
    public string Selector =>
        !string.IsNullOrEmpty(Code) ? Code!
        : Type == HomeSchema.Types.Idea ? HomeNameGuard.FirstLine(Body)
        : Name;

    // A short label for the on-disk reports: code and name as the guichet
    // shows them.
    [JsonIgnore]
    public string Display =>
        !string.IsNullOrEmpty(Code) && Name.Length > 0 ? $"{Code} · {Name}"
        : !string.IsNullOrEmpty(Code) ? Code!
        : Selector;
}

// Counts only: what was exported per manifest type, what the bridge left
// unmapped per old key, which source types were skipped.
internal sealed class HomeReplaySheetReport
{
    [JsonPropertyName("types")]
    public SortedDictionary<string, int> Types { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("unmapped")]
    public SortedDictionary<string, int> Unmapped { get; set; } = new(StringComparer.Ordinal);

    // Source objects of a type the manifest does not carry (Anytype's
    // built-ins: page, collection, file…), by their source type key.
    [JsonPropertyName("skipped_types")]
    public SortedDictionary<string, int> SkippedTypes { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("notes_moved")]
    public int NotesMoved { get; set; }

    [JsonPropertyName("bodies_read")]
    public int BodiesRead { get; set; }

    // Collections whose members could not be read: their memberships, and
    // the zones a floor collection gave its rooms, are absent from the sheet.
    [JsonPropertyName("collections_unread")]
    public int CollectionsUnread { get; set; }

    [JsonIgnore]
    public int Exported => Types.Values.Sum();

    internal static void Increment(SortedDictionary<string, int> counts, string key) =>
        counts[key] = counts.TryGetValue(key, out int value) ? value + 1 : 1;
}
