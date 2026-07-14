using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;

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

    // ── Observability ────────────────────────────────────────────────────────
    //
    // The module's own diagnostics opt-ins, relocated here from the shared
    // Diagnostics page so they sit beside the engine they observe. These write
    // to the LoggingSettings / TelemetrySettings stores — NOT to
    // AutocorrectSettings — so their pushes stay separate from the master
    // switch and the per-app map above. The App's central gates read those two
    // POCOs directly; this VM only mirrors the values to the UI.
    //
    // Log activity — a runtime emission filter, no consent (nothing leaves the
    // device). Decisions and Text are disk-persistence opt-ins, each gated by a
    // consent dialog at the card (declared in the settings manifest). The two
    // telemetry values share PushTelemetryToSettings; the log value has its own
    // PushLoggingToSettings — a single toggle touches only its own store.

    // Log autocorrect activity — the engine's Verbose channel (per-focus probe,
    // learning signals, activity rollup). Off by default; applied corrections and
    // injection failures surface regardless.
    [ObservableProperty]
    public partial bool LogAutocorrectActivity { get; set; }

    // Autocorrect decisions — the per-word decision dataset
    // (autocorrect.decisions.jsonl). Consent-gated, off by default.
    [ObservableProperty]
    public partial bool AutocorrectDecisions { get; set; }

    // Autocorrect text — one consent envelope over the two verbatim captures:
    // the typed-sentence corpus (autocorrect.text.jsonl) and, on enrolled
    // surfaces only, the typing stream (autocorrect.stream.jsonl). The heaviest
    // text capture; consent-gated, independent of Decisions, off by default.
    [ObservableProperty]
    public partial bool AutocorrectText { get; set; }

    public AutocorrectViewModel()
    {
        _isSyncing = true;

        // Seed the observability opt-ins closed; Load() overwrites from the
        // stores under the sync guard.
        LogAutocorrectActivity = false;
        AutocorrectDecisions = false;
        AutocorrectText = false;

        Load();
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var settings = AutocorrectSettingsService.Instance.Current;
            Enabled = settings.Enabled;

            // Observability opt-ins pulled from their own stores, not
            // AutocorrectSettings.
            LogAutocorrectActivity = LoggingSettingsService.Instance.Current.LogAutocorrectActivity;
            var telemetry = TelemetrySettingsService.Instance.Current;
            AutocorrectDecisions = telemetry.AutocorrectDecisions;
            AutocorrectText = telemetry.AutocorrectText;

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

    partial void OnLogAutocorrectActivityChanged(bool value)
    {
        if (_isSyncing) return;
        PushLoggingToSettings();
    }

    partial void OnAutocorrectDecisionsChanged(bool value)
    {
        if (_isSyncing) return;
        PushTelemetryToSettings();
    }

    partial void OnAutocorrectTextChanged(bool value)
    {
        if (_isSyncing) return;
        PushTelemetryToSettings();
    }

    // Observability pushes, kept separate from the AutocorrectSettingsService
    // writes above: each touches only its own store, so flipping a log filter
    // never rewrites the telemetry file and vice-versa.
    private void PushLoggingToSettings()
    {
        LoggingSettingsService.Instance.Current.LogAutocorrectActivity = LogAutocorrectActivity;
        LoggingSettingsService.Instance.Save();
    }

    private void PushTelemetryToSettings()
    {
        var telemetry = TelemetrySettingsService.Instance.Current;
        telemetry.AutocorrectDecisions = AutocorrectDecisions;
        telemetry.AutocorrectText = AutocorrectText;
        TelemetrySettingsService.Instance.Save();
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
