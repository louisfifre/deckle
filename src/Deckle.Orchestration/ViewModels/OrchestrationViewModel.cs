using System.Collections.ObjectModel;

namespace Deckle.Orchestration;

// View-model of the "Apps and games" page: the list of entries, and the add,
// change and remove gestures. Every write goes through
// OrchestrationSettingsService; the rows only call back here.
public sealed class OrchestrationViewModel
{
    private static OrchestrationSettingsService Service => OrchestrationSettingsService.Instance;

    // Ordered by display name so the list reads the same across reloads.
    public ObservableCollection<AppEntryRow> Entries { get; } = new();

    // The folders the scan walks besides Steam, in the order the user added them.
    public ObservableCollection<ScanFolderRow> ScanFolders { get; } = new();

    // The HDR-capable monitors last enumerated, one per GDI device name. Kept
    // so rows built by a reload or an addition get their monitor cards at once.
    private IReadOnlyList<DisplayTarget> _hdrMonitors = [];

    public OrchestrationViewModel() => Load();

    // Rebuilt on every navigation: cheap, and it picks up entries a scan or
    // another writer added while the page sat cached.
    public void Load()
    {
        Entries.Clear();
        foreach (AppEntry entry in Service.Current.Entries
                     .OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            Entries.Add(CreateRow(entry));

        ScanFolders.Clear();
        foreach (string folder in Service.Current.ScanFolders)
            ScanFolders.Add(new ScanFolderRow(folder, OnFolderRemoved));
    }

    // Steam and the chosen folders, on a worker: the scan reads the disk. The
    // list is reloaded afterwards on the caller's (UI) thread.
    public async Task<ScanResult> ScanAsync()
    {
        ScanResult result = await Task.Run(() => RegistryScan.Run(Service));
        Load();
        return result;
    }

    /// <summary>Add a folder to scan. False when it is already listed.</summary>
    public bool AddFolder(string path)
    {
        // TrimEndingDirectorySeparator leaves a drive root ("D:\") whole.
        string trimmed = Path.TrimEndingDirectorySeparator(path);
        if (ScanFolders.Any(f => string.Equals(f.Path, trimmed, StringComparison.OrdinalIgnoreCase)))
            return false;

        ScanFolders.Add(new ScanFolderRow(trimmed, OnFolderRemoved));
        SaveFolders();
        return true;
    }

    private void OnFolderRemoved(ScanFolderRow row)
    {
        ScanFolders.Remove(row);
        SaveFolders();
    }

    private void SaveFolders() =>
        Service.SetScanFolders(ScanFolders.Select(f => f.Path).ToList());

    // The entries as they are now, for filtering the running-app dialog.
    public IReadOnlyList<AppEntry> Snapshot() => Service.Current.Entries;

    /// <summary>Add an executable picked by the user. False when it is already listed.</summary>
    public bool AddExecutable(string path)
    {
        string processName = AppMatcher.NormalizeProcessName(path);
        return Add(new AppEntry
        {
            DisplayName = AppDisplayName.For(path, windowTitle: null, processName),
            Kind = AppKind.App,
            Source = AppSource.Manual,
            ExecutablePath = path,
            ProcessName = processName,
        });
    }

    /// <summary>Add an app picked in the running-app dialog. False when it is already listed.</summary>
    public bool AddRunning(RunningApp app)
    {
        return Add(new AppEntry
        {
            DisplayName = app.DisplayName,
            Kind = AppKind.App,
            Source = AppSource.RunningProcess,
            ExecutablePath = app.ExecutablePath,
            ProcessName = AppMatcher.NormalizeProcessName(app.ProcessName),
        });
    }

    /// <summary>Point a row at another executable. False when another entry already has it.</summary>
    public bool ChangeExecutable(AppEntryRow row, string path)
    {
        var candidate = new AppEntry { ExecutablePath = path };
        var others = Service.Current.Entries.Where(e => e.Id != row.Id).ToList();
        if (AppMatcher.IsDuplicate(others, candidate)) return false;

        Service.UpdateEntry(row.Id, entry =>
        {
            entry.ExecutablePath = path;
            entry.ProcessName = AppMatcher.NormalizeProcessName(path);
        });
        Refresh(row);
        return true;
    }

    // True when the entry repeats one already listed. Beyond the service's
    // rule (same exe or install folder), an app whose path could not be read
    // (an elevated process) repeats a path-less entry of the same name.
    public bool IsListed(AppEntry candidate)
    {
        var entries = Service.Current.Entries;
        if (AppMatcher.IsDuplicate(entries, candidate)) return true;
        return candidate.ExecutablePath is null
            && entries.Any(e => e.ExecutablePath is null
                && e.InstallDirectory is null
                && string.Equals(e.ProcessName, candidate.ProcessName, StringComparison.OrdinalIgnoreCase));
    }

    private bool Add(AppEntry entry)
    {
        if (IsListed(entry) || !Service.AddEntry(entry)) return false;

        // Insert in place rather than reload, so only the new row animates in.
        int index = 0;
        while (index < Entries.Count
               && StringComparer.CurrentCultureIgnoreCase.Compare(Entries[index].DisplayName, entry.DisplayName) <= 0)
            index++;
        Entries.Insert(index, CreateRow(entry));
        return true;
    }

    /// <summary>
    /// Show one HDR card per HDR-capable monitor under every entry. A monitor
    /// that cannot do HDR gets no card (hidden, not greyed); clone mode lists
    /// one device name twice, kept once.
    /// </summary>
    internal void SetMonitors(IReadOnlyList<DisplayTarget> targets)
    {
        _hdrMonitors = targets
            .Where(t => t.HdrSupported)
            .DistinctBy(t => t.GdiDeviceName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t.GdiDeviceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (AppEntryRow row in Entries) row.SetMonitors(_hdrMonitors);
    }

    private AppEntryRow CreateRow(AppEntry entry)
    {
        var row = new AppEntryRow(entry, OnRowEnabledChanged, OnRowKindChanged, OnRowRemoved, OnRowHdrChanged);
        row.SetMonitors(_hdrMonitors);
        return row;
    }

    // Leave removes the key: an absent monitor and Leave mean the same.
    private static void OnRowHdrChanged(AppEntryRow row, string device, HdrRule rule) =>
        Service.UpdateEntry(row.Id, entry =>
        {
            if (rule == HdrRule.Leave) entry.Rules.HdrByMonitor.Remove(device);
            else entry.Rules.HdrByMonitor[device] = rule;
        });

    private void Refresh(AppEntryRow row)
    {
        AppEntry? stored = Service.Current.Entries.Find(e => e.Id == row.Id);
        if (stored is not null) row.Apply(stored);
    }

    private static void OnRowEnabledChanged(AppEntryRow row, bool enabled) =>
        Service.UpdateEntry(row.Id, entry => entry.Enabled = enabled);

    private static void OnRowKindChanged(AppEntryRow row, AppKind kind) =>
        Service.UpdateEntry(row.Id, entry => entry.Kind = kind);

    private void OnRowRemoved(AppEntryRow row)
    {
        Service.RemoveEntry(row.Id);
        Entries.Remove(row);
    }
}
