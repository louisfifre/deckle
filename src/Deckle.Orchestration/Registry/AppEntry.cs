namespace Deckle.Orchestration;

// One app or game in the registry. A plain serializable POCO: the rules that
// read it (matching, duplicates) live in AppMatcher.
public sealed class AppEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public AppKind Kind { get; set; } = AppKind.App;

    public AppSource Source { get; set; } = AppSource.Manual;

    // Full path of the executable, when known. A Steam game whose exe could not
    // be picked has none and matches by InstallDirectory instead.
    public string? ExecutablePath { get; set; }

    // Folder the app is installed in (Steam and folder scans). Any process whose
    // image lives under it belongs to this entry.
    public string? InstallDirectory { get; set; }

    // Executable file stem, without directory or ".exe", matched case-insensitively.
    public string ProcessName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string? SteamAppId { get; set; }

    public AppRules Rules { get; set; } = new();

    // Deep copy for OrchestrationSettingsService.UpdateEntry: the mutation runs
    // on the copy, so a reader holding the old list never sees a half-changed entry.
    internal AppEntry Clone() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Kind = Kind,
        Source = Source,
        ExecutablePath = ExecutablePath,
        InstallDirectory = InstallDirectory,
        ProcessName = ProcessName,
        Enabled = Enabled,
        SteamAppId = SteamAppId,
        Rules = Rules.Clone(),
    };
}
