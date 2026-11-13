using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deckle.App.SelfCheck;

// What the self-check run saw, and the verdict drawn from it. The file is the
// run's only output the caller must read (scripts/self-check.ps1), so it is
// written after every step, not once at the end: a run that dies in a native
// crash leaves the report of the last step it started, with Completed false and
// Current naming the page it was opening. The crash exit code says how the
// process died; the report says where.
//
// The verdict lives here, apart from the code that drives the windows, so it
// can be tested without a window.
internal sealed class SelfCheckReport
{
    public const string FileName = "report.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Version { get; set; } = "";
    public string Build { get; set; } = "";
    public string DataRoot { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }

    // True once the run reached its end. Absent or false in a file means the
    // process stopped before: Current is the step it was in.
    public bool Completed { get; set; }
    public string? Current { get; set; }

    // A step that threw out of the run (not a page that failed to load, which
    // is a surface with Loaded false).
    public string? Fatal { get; set; }

    public List<SelfCheckSurface> Surfaces { get; set; } = [];
    public SelfCheckMicrophone? Microphone { get; set; }
    public List<SelfCheckLogEvent> Errors { get; set; } = [];

    // What the run composed and what it left out, with the reason, so a reader
    // knows how far "passed" reaches.
    public List<string> Composed { get; set; } = [];
    public List<SelfCheckLeftOut> LeftOut { get; set; } = [];

    public List<string> Failures { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public string Outcome { get; set; } = "failed";

    // The one window or view a targeted run was asked for (SelfCheckTarget's
    // name); null for a full run. "passed" then reaches that target only: the
    // other windows and the microphone were not looked at.
    public string? Only { get; set; }

    // The views the target's window offered, recorded when the target matched
    // none of them, so the failure can say what it could have been.
    public List<string>? Views { get; set; }

    // Recomputes Failures, Warnings and Outcome from what was recorded.
    public void Judge()
    {
        var failures = new List<string>();
        var warnings = new List<string>();

        if (Fatal is not null)
            failures.Add($"The run stopped on an exception: {Fatal}");
        if (!Completed)
            failures.Add($"The run did not reach its end; it was in: {Current ?? "(before the first step)"}.");

        foreach (SelfCheckSurface surface in Surfaces)
        {
            if (!surface.Loaded)
                failures.Add($"{surface.Name}: did not load (navigation did not complete in {surface.Ms} ms).");
            else if (surface.Png is null)
                failures.Add($"{surface.Name}: loaded but its image could not be taken ({surface.Error ?? "no pixels"}).");
            else if (surface.PopupsStillOpen > 0)
                failures.Add($"{surface.Name}: its image was taken with {surface.PopupsStillOpen} popup(s) open over the page.");
        }

        if (Only is null)
        {
            foreach (string window in SelfCheckTarget.Windows)
            {
                if (!Surfaces.Any(s => s.Window == window))
                    failures.Add($"{window}: never walked, so nothing was checked there.");
            }
        }
        else if (Completed && !Surfaces.Any(s => s.Name == Only || (s.Window == Only && !Only.Contains('/'))))
        {
            // Said only of a run that reached its end: before that, the walk
            // may simply not have arrived there yet.
            failures.Add(Views is { Count: > 0 }
                ? $"{Only}: no such view, so nothing was checked. The views there are: {string.Join(", ", Views)}."
                : $"{Only}: never walked, so nothing was checked there.");
        }

        if (Microphone is null)
        {
            // A targeted run leaves the probe out; a full run must have made it.
            if (Only is null) failures.Add("microphone: the probe never ran.");
        }
        else
        {
            switch (Microphone.Outcome)
            {
                case SelfCheckMicrophone.Ok:
                    break;
                case SelfCheckMicrophone.DeviceMissing:
                case SelfCheckMicrophone.DeviceBusy:
                case SelfCheckMicrophone.DeviceRefused:
                    // A machine may have no microphone, or another application
                    // may hold it: the library works, which is what is checked.
                    warnings.Add($"microphone: {Microphone.Outcome} ({Microphone.ErrorName}); the native library loaded.");
                    break;
                default:
                    failures.Add(
                        $"microphone: {Microphone.Outcome} at stage {Microphone.Stage ?? "?"} ({Microphone.ErrorName} {Microphone.ErrorCode}).");
                    break;
            }
        }

        if (Errors.Count > 0)
            failures.Add($"{Errors.Count} log event(s) at Error or Critical during the run: "
                + string.Join(", ", Errors.Select(e => e.Event).Distinct()));

        Failures = failures;
        Warnings = warnings;
        Outcome = failures.Count == 0 ? "passed" : "failed";
    }

    public string ToJson()
    {
        Judge();
        return JsonSerializer.Serialize(this, Json);
    }

    // Written whole to a temp name then moved over the report, so a reader that
    // arrives during a write sees the previous complete file, never half of one.
    public void Save(string folder)
    {
        string path = Path.Combine(folder, FileName);
        string temp = path + ".tmp";
        File.WriteAllText(temp, ToJson());
        File.Move(temp, path, overwrite: true);
    }

    public static SelfCheckReport? Parse(string json)
    {
        try { return JsonSerializer.Deserialize<SelfCheckReport>(json, Json); }
        catch (JsonException) { return null; }
    }
}

// One window or one page of a paged window, as the run saw it.
//   Window   the stable name: settings, log, mail, atelier.
//   Page     the page slug for a paged window, null otherwise.
//   Loaded   the surface was on screen and loaded before the timeout.
//   Ms       how long that took.
//   Png      the image, relative to the report's folder; null when none was taken.
//   PopupsClosed    popups found open over the window and closed before the image
//                   (a flyout the walk left open); informational.
//   PopupsStillOpen popups that stayed open: the image shows something over the page.
//   Technique       how the image was made: page-render (the page's whole XAML
//                   content), window-capture (DWM pixels of the window) or
//                   window-render (the window's XAML content, when DWM gave none).
//   Width, Height   the image's size in pixels; 0 when the technique does not say.
//   Parts           the files, in order, when a page was too tall for one image.
//   Error           why an image could not be made, when it threw.
internal sealed record SelfCheckSurface(
    string Window, string? Page, string? Label, bool Loaded, long Ms, string? Png,
    int PopupsClosed = 0, int PopupsStillOpen = 0,
    string? Technique = null, int Width = 0, int Height = 0,
    string[]? Parts = null, string? Error = null)
{
    [JsonIgnore]
    public string Name => Page is null ? Window : $"{Window}/{Page}";
}

internal sealed record SelfCheckLeftOut(string Piece, string Why);

// One Error or Critical line of the run's own log.jsonl.
internal sealed record SelfCheckLogEvent(string Timestamp, string Level, string Provider, string Event, string Message);

// How the microphone probe ended, with the stage that refused. The distinction
// that matters: the native library not loading (a packaging fault of the build)
// against a device that is absent, held or refusing (a fact about the machine).
internal sealed record SelfCheckMicrophone(string Outcome, string? Stage, string ErrorName, string ErrorCode)
{
    public const string Ok = "ok";
    public const string LoadFailed = "load_failed";
    public const string InitializeFailed = "initialize_failed";
    public const string DeviceMissing = "device_missing";
    public const string DeviceBusy = "device_busy";
    public const string DeviceRefused = "device_refused";

    // From a probe: Ok, Stage, the classified kind, the code and its symbol.
    // The stage decides first, because a library that did not load carries the
    // same code (-10000) a refusing device can.
    public static SelfCheckMicrophone From(
        bool ok, string? stage, Deckle.Audio.MicErrorKind kind, string errorName, string errorCode)
    {
        if (ok) return new(Ok, null, "OK", "0");

        string outcome = stage switch
        {
            "load" => LoadFailed,
            "initialize" => InitializeFailed,
            _ => kind switch
            {
                Deckle.Audio.MicErrorKind.NotDetected => DeviceMissing,
                Deckle.Audio.MicErrorKind.InUse => DeviceBusy,
                _ => DeviceRefused,
            },
        };
        return new(outcome, stage, errorName, errorCode);
    }
}

// Reads the Error and Critical lines of a log.jsonl. Pure, so a test hands it
// lines. A line that is not JSON (a write cut by a crash) is skipped.
internal static class SelfCheckLogReader
{
    // EventLevel names in the log's "level" field.
    private static readonly HashSet<string> Severe = new(StringComparer.Ordinal) { "Critical", "Error" };

    public static List<SelfCheckLogEvent> ReadSevere(IEnumerable<string> lines)
    {
        var found = new List<SelfCheckLogEvent>();
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;
                string level = Text(root, "level");
                if (!Severe.Contains(level)) continue;

                string message = Text(root, "message");
                int firstLineEnd = message.IndexOf('\n');
                if (firstLineEnd >= 0) message = message[..firstLineEnd];

                found.Add(new SelfCheckLogEvent(
                    Text(root, "timestamp"), level, Text(root, "provider"), Text(root, "event"), message));
            }
            catch (JsonException)
            {
            }
        }
        return found;
    }

    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    // The file is held open for writing by the log sink: read it sharing.
    public static List<SelfCheckLogEvent> ReadSevereFromFile(string path)
    {
        if (!File.Exists(path)) return [];

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return ReadSevere(lines);
    }
}
