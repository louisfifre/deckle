using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    // The vocabulary packs the build ships, in shipped order — every one is
    // listed whether or not the user has met it, so what a pack brings can be
    // read before activating it. Unlike Apps this list is fixed by the build,
    // not enumerated from the settings file.
    public ObservableCollection<AutocorrectPackRow> Packs { get; } = new();

    // The exclusion register: words the user pulled out of correction's reach,
    // whatever lexicon carried them. Consultable and reversible here — the
    // settings mirror of a gesture that will also be born in the correction
    // inlay, once that surface exists.
    public ObservableCollection<AutocorrectExcludedWordRow> ExcludedWords { get; } = new();

    // What the user is typing into the exclusion field. Not persisted — it
    // becomes an entry only on the add gesture.
    [ObservableProperty]
    public partial string NewExclusion { get; set; } = string.Empty;

    // ── Observability ────────────────────────────────────────────────────────
    //
    // The module owns only its purpose-specific dataset consents here.
    // Operational-detail admission is edited centrally on DiagnosticsPage.

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
            var telemetry = TelemetrySettingsService.Instance.Current;
            AutocorrectDecisions = telemetry.AutocorrectDecisions;
            AutocorrectText = telemetry.AutocorrectText;

            // The dilution figures come from each pack's shipped manifest, read
            // here rather than held live: they are fabrication output and only
            // change when a new pack ships.
            string dataDir = AutocorrectLexiconArtifacts.DataDirectory;
            Packs.Clear();
            foreach (DomainPack pack in DomainPack.Shipped)
            {
                Packs.Add(new AutocorrectPackRow(
                    pack,
                    settings.IsDomainPackActive(pack.Id),
                    DomainPackManifest.TryLoad(dataDir, pack),
                    OnPackToggled));
            }

            ExcludedWords.Clear();
            foreach (string word in settings.ExcludedWords)
                ExcludedWords.Add(new AutocorrectExcludedWordRow(word, OnWordIncluded));

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

    private void PushTelemetryToSettings()
    {
        var telemetry = TelemetrySettingsService.Instance.Current;
        telemetry.AutocorrectDecisions = AutocorrectDecisions;
        telemetry.AutocorrectText = AutocorrectText;
        TelemetrySettingsService.Instance.Save();
    }

    // Activating a pack changes the effective lexicon, which is merged at engine
    // build — the App notices the key change and rebuilds. Nothing to do here
    // beyond persisting the choice.
    private static void OnPackToggled(AutocorrectPackRow row, bool active)
        => AutocorrectSettingsService.Instance.SetDomainPackActive(row.PackId, active);

    // Excludes what is in the field. The service owns the normalization and
    // answers with the form it registered, so the list shows the stored key
    // rather than what was typed. Text that cannot name a single word — blank,
    // or several words — clears the field and adds nothing: the add button is
    // already disabled for the blank case, and a constrained control beats an
    // error message.
    [RelayCommand]
    private void ExcludeWord()
    {
        string? excluded = AutocorrectSettingsService.Instance.ExcludeWord(NewExclusion);
        NewExclusion = string.Empty;
        if (excluded is null) return;

        if (ExcludedWords.Any(row => string.Equals(row.Word, excluded, StringComparison.Ordinal)))
            return;

        int index = 0;
        while (index < ExcludedWords.Count
               && string.CompareOrdinal(ExcludedWords[index].Word, excluded) < 0)
            index++;
        ExcludedWords.Insert(index, new AutocorrectExcludedWordRow(excluded, OnWordIncluded));
    }

    private void OnWordIncluded(AutocorrectExcludedWordRow row)
    {
        AutocorrectSettingsService.Instance.IncludeWord(row.Word);
        ExcludedWords.Remove(row);
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
