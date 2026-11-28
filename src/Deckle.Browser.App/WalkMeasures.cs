using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Deckle.Browser.App;

// What a whole walk measured, kept for comparison between runs. measures.txt
// beside the report stays the human's reading of one run; this adds one JSON
// line per run to a history file a later session reads to see whether the
// browser got faster or slower: the time, the commit, the build, and every
// figure the walks produced. A figure a run did not reach stays null.
//
// The history is under the repository's artifacts, in
// self-check/browser/measures-history.jsonl: scripts/self-check.ps1 keeps no
// per-app history of its own (its run folders are in the temp folder and go
// with it), and neither does Folders. The folder is found from the running
// executable, which a build puts under <repo>/artifacts/bin; a run started
// from elsewhere writes the line beside its report instead.
internal sealed class WalkMeasures
{
    public const string HistoryFileName = "measures-history.jsonl";

    // The time from the process's start to the window drawn (the first page
    // loaded or not): what a person waits at launch.
    public double? LaunchToWindowReadyMs { get; set; }

    // Navigation start to NavigationCompleted of a local page, in the first
    // web view of the run, which also pays for the engine's own start.
    public double? LocalPageMs { get; set; }

    // The web view processes with one page loaded.
    public double? WebViewMb { get; set; }
    public int? WebViewProcesses { get; set; }

    // Eight pinned sites and a tab, awake and then suspended.
    public double? Pinned8LoadedMb { get; set; }
    public double? Pinned8SuspendedMb { get; set; }

    // Selecting a suspended pinned site until its page answers, the average of three.
    public double? WakeMs { get; set; }

    // The blocker's engine: built from the walk's one local rule (no list is
    // fetched), so the build and the count are those of that rule; "lists"
    // would say the real lists were loaded.
    public double? EngineBuildMs { get; set; }
    public int? EngineRules { get; set; }
    public string? EngineSource { get; set; }

    // Each gesture's duration in milliseconds, from the press to what it
    // changes being visible, by the gesture's name.
    public Dictionary<string, double> GestureMs { get; } = [];
    public List<string> GestureFailures { get; } = [];

    // The UI thread's stalls (ticks of the 100 ms watch more than 200 ms late).
    public long? UiStalls { get; set; }
    public long? WorstStallMs { get; set; }

    public string Outcome { get; set; } = "failed";

    // The one line of the run: field names are camelCase, times in ms, memory in MB.
    public string ToJsonLine(DateTimeOffset at, string commit, string build)
    {
        var line = new Dictionary<string, object?>
        {
            ["utc"] = at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["commit"] = commit,
            ["build"] = build,
            ["outcome"] = Outcome,
            ["launchToWindowReadyMs"] = Round(LaunchToWindowReadyMs),
            ["localPageMs"] = Round(LocalPageMs),
            ["webViewMb"] = Round(WebViewMb),
            ["webViewProcesses"] = WebViewProcesses,
            ["pinned8LoadedMb"] = Round(Pinned8LoadedMb),
            ["pinned8SuspendedMb"] = Round(Pinned8SuspendedMb),
            ["wakeMs"] = Round(WakeMs),
            ["engineBuildMs"] = Round(EngineBuildMs),
            ["engineRules"] = EngineRules,
            ["engineSource"] = EngineSource,
            ["gestureMs"] = GestureMs.ToDictionary(g => g.Key, g => (double?)Math.Round(g.Value, 1)),
            ["gestureFailures"] = GestureFailures,
            ["uiStalls"] = UiStalls,
            ["worstStallMs"] = WorstStallMs,
        };
        return JsonSerializer.Serialize(line);
    }

    private static double? Round(double? value) => value is { } v ? Math.Round(v, 1) : null;

    /// <summary>Appends this run's line to the history, and says where.</summary>
    public string AppendToHistory(string outputFolder, DateTimeOffset at)
    {
        string folder = HistoryFolder() ?? outputFolder;
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, HistoryFileName);
        File.AppendAllText(path, ToJsonLine(at, Commit(), BuildName) + Environment.NewLine);
        return path;
    }

    private static string BuildName =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    // <repo>/artifacts/self-check/browser, when the executable stands under <repo>/artifacts.
    private static string? HistoryFolder()
    {
        for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (string.Equals(at.Name, "artifacts", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(at.FullName, "self-check", "browser");
        }
        return null;
    }

    // The commit the build came from: the repository's HEAD at the run, read by git
    // (the exe's own version carries only the tag). "unknown" when git is not there.
    private static string Commit()
    {
        try
        {
            string? repo = HistoryFolder() is { } folder ? Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(folder))) : null;
            if (repo is null) return "unknown";
            var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("-C");
            start.ArgumentList.Add(repo);
            start.ArgumentList.Add("rev-parse");
            start.ArgumentList.Add("--short=8");
            start.ArgumentList.Add("HEAD");
            using Process? process = Process.Start(start);
            if (process is null) return "unknown";
            string head = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return head.Length > 0 ? head : "unknown";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return "unknown";
        }
    }
}
