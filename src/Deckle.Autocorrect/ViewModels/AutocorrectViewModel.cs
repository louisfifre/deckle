using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Autocorrect;

// ViewModel for AutocorrectPage — what belongs on a family's landing surface:
// the master switch, the exclusion register, and the observability opt-ins. The
// two collections keyed by a child surface left with it — LexicalDomainsViewModel
// owns the domains, AppsEnrolledViewModel the per-app decisions. The exclusions
// stayed: an exclusion holds against every domain and every app at once, so it
// belongs to the family rather than to either child.
//
// Same Load/_isSyncing shape as TrackpadViewModel: Load() seeds the bound state
// with writes suppressed, then each user change routes back through the service
// that owns it.
public sealed partial class AutocorrectViewModel : ObservableObject
{
    private bool _isSyncing;

    // Master switch — when off, the engine corrects nothing, in any app.
    [ObservableProperty]
    public partial bool Enabled { get; set; }

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

            ExcludedWords.Clear();
            foreach (string word in settings.ExcludedWords)
                ExcludedWords.Add(new AutocorrectExcludedWordRow(word, OnWordIncluded));
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
}
