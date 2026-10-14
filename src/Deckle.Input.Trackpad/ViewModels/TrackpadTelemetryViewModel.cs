using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;

namespace Deckle.Input.Trackpad;

// ── TrackpadTelemetryViewModel ────────────────────────────────────────────────
//
// The raw-frame recording as the module's contribution to Settings >
// Diagnostics, the one editor of everything Deckle records about itself
// (2026-10-14; the card sat in a Diagnostics section of the Trackpad page
// before). It writes every raw contact frame to a JSONL file under the
// telemetry folder, independent of the module's master switch: it captures
// with the recognizer off. The module keeps the model; the Diagnostics page
// composes the card through DiagnosticsRegistry and resolves its words from
// this assembly.
//
// Unlike the consent-gated datasets, this one has a default in
// TrackpadSettings and no consent dialog: it joins the page's reset.
public sealed partial class TrackpadTelemetryViewModel : ObservableObject
{
    private bool _isSyncing;

    [ObservableProperty]
    public partial bool RecordFrames { get; set; }

    public TrackpadTelemetryViewModel()
    {
        _isSyncing = true;
        Load();
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            RecordFrames = TrackpadSettingsService.Instance.Current.RecordFrames;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnRecordFramesChanged(bool value)
    {
        if (_isSyncing) return;
        TrackpadSettingsService.Instance.Current.RecordFrames = value;
        TrackpadSettingsService.Instance.Save();
    }

    public IReadOnlyList<SettingDescriptor> Settings =>
    [
        Setting.Toggle("TrackpadPage_RecordFramesCard",
            () => RecordFrames,
            value => RecordFrames = value,
            defaultValue: () => new TrackpadSettings().RecordFrames),
    ];

    // The module's entry in the Diagnostics registry.
    public static DiagnosticsContribution Contribution(int order) => new(
        "trackpad", order,
        () =>
        {
            var model = new TrackpadTelemetryViewModel();
            // Off is this dataset's default, so the composer's ResetAll already
            // turns it off; TurnOff says the same for the page's Reset all.
            return new DiagnosticsSource(model, model.Settings, model.Load, () => model.RecordFrames = false);
        });
}
