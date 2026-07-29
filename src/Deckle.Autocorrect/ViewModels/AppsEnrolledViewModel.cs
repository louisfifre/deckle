using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Autocorrect;

// ViewModel for AppsEnrolledPage — the per-app decision map, and nothing else.
// Each user change routes back through AutocorrectSettingsService, which owns
// the decision write (reference-swap under its lock).
//
// The list is rebuilt on every Load() — cheap (a handful of apps) and the
// simplest way to reflect a decision the enrollment toast may have written while
// the page sat cached.
public sealed partial class AppsEnrolledViewModel : ObservableObject
{
    // Mirror of the module's master switch — read-only here, flipped on the
    // parent page. The page collapses its whole section while it is false: with
    // autocorrect off nothing is corrected anywhere, so no per-app row applies
    // (mask-never-grey).
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    // The apps the user has decided on (enabled or declined), ordered by
    // display name so the list is stable across reloads.
    public ObservableCollection<AutocorrectAppRow> Apps { get; } = new();

    public AppsEnrolledViewModel() => Load();

    public void Load()
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
