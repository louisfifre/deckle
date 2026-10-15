using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Autocorrect;

// ── AutocorrectTelemetryViewModel ─────────────────────────────────────────────
//
// The module's two dataset consents as its contribution to Settings >
// Diagnostics, the one editor of everything Deckle records about itself
// (2026-10-14; the cards sat in a Diagnostics section of the Autocorrect page
// before). The module keeps the model and the consents; the Diagnostics page
// composes the cards through DiagnosticsRegistry and resolves their words from
// this assembly.
//
// Decisions is the per-word decision dataset (autocorrect.decisions.jsonl).
// Text is one consent envelope over the two verbatim captures: the
// typed-sentence corpus (autocorrect.text.jsonl) and, on enrolled surfaces
// only, the typing stream (autocorrect.stream.jsonl). Text is NOT gated by
// Decisions, so they stay two independent leaves, never a masking Group. Both
// are consent-gated through the Catalog registry and off by default. Each
// reads its default from TelemetrySettings, so each card has its reset (Louis,
// 2026-10-15: every setting with a default gets its reset); the reset only
// turns an opt-in off, a write the consent gate does not hold.
public sealed partial class AutocorrectTelemetryViewModel : ObservableObject
{
    private bool _isSyncing;

    [ObservableProperty]
    public partial bool AutocorrectDecisions { get; set; }

    [ObservableProperty]
    public partial bool AutocorrectText { get; set; }

    public AutocorrectTelemetryViewModel()
    {
        _isSyncing = true;
        AutocorrectDecisions = false;
        AutocorrectText = false;
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var telemetry = TelemetrySettingsService.Instance.Current;
            AutocorrectDecisions = telemetry.AutocorrectDecisions;
            AutocorrectText = telemetry.AutocorrectText;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnAutocorrectDecisionsChanged(bool value)
    {
        if (_isSyncing) return;
        Push();
    }

    partial void OnAutocorrectTextChanged(bool value)
    {
        if (_isSyncing) return;
        Push();
    }

    private void Push()
    {
        var telemetry = TelemetrySettingsService.Instance.Current;
        telemetry.AutocorrectDecisions = AutocorrectDecisions;
        telemetry.AutocorrectText = AutocorrectText;
        TelemetrySettingsService.Instance.Save();
    }

    public IReadOnlyList<SettingDescriptor> Settings =>
    [
        Setting.Toggle("AutocorrectDecisionsCard",
            () => AutocorrectDecisions,
            value => AutocorrectDecisions = value,
            defaultValue: () => new TelemetrySettings().AutocorrectDecisions,
            confirmOnEnable: TelemetryConsent.RequestAutocorrectDecisions),
        Setting.Toggle("AutocorrectTextCard",
            () => AutocorrectText,
            value => AutocorrectText = value,
            defaultValue: () => new TelemetrySettings().AutocorrectText,
            confirmOnEnable: TelemetryConsent.RequestAutocorrectText),
    ];

    // Called by the Diagnostics page's Reset all. Outside the sync guard, so
    // each change pushes and saves like a user's flip.
    public void TurnOff()
    {
        AutocorrectDecisions = false;
        AutocorrectText = false;
    }

    // The module's entry in the Diagnostics registry.
    public static DiagnosticsContribution Contribution(int order) => new(
        "autocorrect", order,
        () =>
        {
            var model = new AutocorrectTelemetryViewModel();
            return new DiagnosticsSource(model, model.Settings, model.Load, model.TurnOff);
        });
}
