using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Orchestration;

// One HDR-capable monitor under an entry: what to do with its HDR while the
// app runs. The choice calls back into the entry row, which routes the write
// to OrchestrationSettingsService.
public sealed partial class MonitorHdrRow : ObservableObject
{
    private readonly Action<MonitorHdrRow, HdrRule> _onRuleChanged;

    // Same hydration guard as AppEntryRow.
    private bool _syncing;

    /// <summary>GDI device name, the key of AppRules.HdrByMonitor.</summary>
    public string Device { get; }

    public string FriendlyName { get; }

    // ComboBox position: 0 = Leave, 1 = On, 2 = Off (the HdrRule order).
    [ObservableProperty]
    public partial int RuleIndex { get; set; }

    public MonitorHdrRow(string device, string friendlyName, HdrRule rule, Action<MonitorHdrRow, HdrRule> onRuleChanged)
    {
        Device = device;
        FriendlyName = friendlyName;
        _onRuleChanged = onRuleChanged;
        Apply(rule);
    }

    public void Apply(HdrRule rule)
    {
        _syncing = true;
        try
        {
            RuleIndex = (int)rule;
        }
        finally
        {
            _syncing = false;
        }
    }

    partial void OnRuleIndexChanged(int value)
    {
        if (value < 0 || _syncing) return;
        _onRuleChanged(this, (HdrRule)value);
    }
}
