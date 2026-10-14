using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deckle.Catalog;

namespace Deckle.Orchestration;

// One row of the apps and games list. The row never touches persistence: a
// toggle, a kind change or a removal calls back into the view-model, which
// routes to OrchestrationSettingsService, so the write stays in one place.
public sealed partial class AppEntryRow : ObservableObject
{
    private readonly Action<AppEntryRow, bool> _onEnabledChanged;
    private readonly Action<AppEntryRow, AppKind> _onKindChanged;
    private readonly Action<AppEntryRow> _onRemoved;
    private readonly Action<AppEntryRow, string, HdrRule> _onHdrChanged;

    // The entry's HDR rules as last read or written, so monitor rows built
    // after the monitors are enumerated show the stored choice.
    private Dictionary<string, HdrRule> _hdrRules = new(StringComparer.OrdinalIgnoreCase);

    // Set while the row is filled from the stored entry, so hydration is not
    // mistaken for a user change.
    private bool _syncing;

    public string Id { get; }

    public string DisplayName { get; }

    [ObservableProperty]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial string? ExecutablePath { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    // ComboBox position: 0 = App, 1 = Game (the AppKind order).
    [ObservableProperty]
    public partial int KindIndex { get; set; }

    // The cards inside the expander: kind, executable, then one per
    // HDR-capable monitor.
    public ObservableCollection<object> Items { get; } = new();

    public AppEntryRow(
        AppEntry entry,
        Action<AppEntryRow, bool> onEnabledChanged,
        Action<AppEntryRow, AppKind> onKindChanged,
        Action<AppEntryRow> onRemoved,
        Action<AppEntryRow, string, HdrRule> onHdrChanged)
    {
        Id = entry.Id;
        DisplayName = entry.DisplayName;
        _onEnabledChanged = onEnabledChanged;
        _onKindChanged = onKindChanged;
        _onRemoved = onRemoved;
        _onHdrChanged = onHdrChanged;
        Items.Add(new EntryKindItem(this));
        Items.Add(new EntryExecutableItem(this));
        Apply(entry);
    }

    // Replace the monitor cards with one per given monitor (HDR-capable ones only).
    internal void SetMonitors(IReadOnlyList<DisplayTarget> monitors)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
            if (Items[i] is MonitorHdrRow) Items.RemoveAt(i);

        foreach (DisplayTarget monitor in monitors)
        {
            HdrRule rule = _hdrRules.GetValueOrDefault(monitor.GdiDeviceName, HdrRule.Leave);
            Items.Add(new MonitorHdrRow(monitor.GdiDeviceName, monitor.FriendlyName, rule, OnMonitorRuleChanged));
        }
    }

    private void OnMonitorRuleChanged(MonitorHdrRow monitor, HdrRule rule)
    {
        if (rule == HdrRule.Leave) _hdrRules.Remove(monitor.Device);
        else _hdrRules[monitor.Device] = rule;
        _onHdrChanged(this, monitor.Device, rule);
    }

    // Refresh the row from the stored entry without echoing changes back.
    public void Apply(AppEntry entry)
    {
        _syncing = true;
        try
        {
            ExecutablePath = entry.ExecutablePath;
            Description = entry.ExecutablePath ?? entry.InstallDirectory;
            Enabled = entry.Enabled;
            KindIndex = (int)entry.Kind;
            _hdrRules = new Dictionary<string, HdrRule>(entry.Rules.HdrByMonitor, StringComparer.OrdinalIgnoreCase);
            foreach (MonitorHdrRow monitor in Items.OfType<MonitorHdrRow>())
                monitor.Apply(_hdrRules.GetValueOrDefault(monitor.Device, HdrRule.Leave));
        }
        finally
        {
            _syncing = false;
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_syncing) return;
        _onEnabledChanged(this, value);
    }

    partial void OnKindIndexChanged(int value)
    {
        if (value < 0) return;
        var kind = (AppKind)value;
        if (_syncing) return;
        _onKindChanged(this, kind);
    }

    [RelayCommand]
    private void Remove() => _onRemoved(this);
}
