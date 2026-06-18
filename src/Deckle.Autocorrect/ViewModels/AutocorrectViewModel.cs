using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Autocorrect;

// ViewModel for AutocorrectPage — bridges AutocorrectSettings (the master
// switch and the per-app decision map) to the XAML. Same Load/_isSyncing shape
// as TrackpadViewModel: Load() seeds the bound state with writes suppressed,
// then each user change routes back through AutocorrectSettingsService, which
// owns the decision write (reference-swap under its lock).
//
// The list is rebuilt on every Load() — cheap (a handful of apps) and the
// simplest way to reflect a decision the enrollment toast may have written
// while the page sat cached.
public sealed partial class AutocorrectViewModel : ObservableObject
{
    private bool _isSyncing;

    // Master switch — when off, the engine corrects nothing, in any app.
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    // The apps the user has decided on (enabled or declined), ordered by
    // display name so the list is stable across reloads.
    public ObservableCollection<AutocorrectAppRow> Apps { get; } = new();

    public AutocorrectViewModel()
    {
        _isSyncing = true;
        Load();
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var settings = AutocorrectSettingsService.Instance.Current;
            Enabled = settings.Enabled;

            Apps.Clear();
            foreach (var entry in settings.Apps
                         .OrderBy(kv => Humanize(kv.Key), StringComparer.CurrentCultureIgnoreCase))
            {
                Apps.Add(new AutocorrectAppRow(
                    entry.Key, Humanize(entry.Key), entry.Value, OnRowToggled, OnRowForgotten));
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        AutocorrectSettingsService.Instance.SetEnabled(value);
    }

    private static void OnRowToggled(AutocorrectAppRow row, bool enabled)
        => AutocorrectSettingsService.Instance.SetDecision(row.ProcessName, enabled);

    private void OnRowForgotten(AutocorrectAppRow row)
    {
        AutocorrectSettingsService.Instance.RemoveDecision(row.ProcessName);
        Apps.Remove(row);
    }

    // Process name → a friendly label. Until the enrollment path captures the
    // real product name (the first-party pass), title-case the executable
    // stem: "anytype" -> "Anytype", "claude" -> "Claude".
    private static string Humanize(string process)
    {
        if (string.IsNullOrWhiteSpace(process)) return process;
        return char.ToUpper(process[0], CultureInfo.CurrentCulture) + process[1..];
    }
}
