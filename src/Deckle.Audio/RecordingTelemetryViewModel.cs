using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Audio;

// ── RecordingTelemetryViewModel ───────────────────────────────────────────────
//
// The microphone-telemetry opt-in (the per-recording RMS distribution) as the
// module's contribution to Settings > Diagnostics, the one editor of everything
// Deckle records about itself (2026-10-14; the card sat in a Diagnostics
// section of the Recording page before). The module keeps the model and the
// consent; the Diagnostics page composes the card through DiagnosticsRegistry
// and resolves its words from this assembly.
//
// The opt-in persists in its own store (TelemetrySettingsService), not in
// CaptureSettings. The enable is gated by the shell's consent dialog through
// the Catalog registry (TelemetryConsent.RequestMicrophone). No defaultValue: a
// privacy opt-in carries no per-row reset.
public sealed partial class RecordingTelemetryViewModel : ObservableObject
{
    private bool _isSyncing;

    [ObservableProperty]
    public partial bool MicrophoneTelemetry { get; set; }

    public RecordingTelemetryViewModel()
    {
        _isSyncing = true;
        MicrophoneTelemetry = false;
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            MicrophoneTelemetry = TelemetrySettingsService.Instance.Current.MicrophoneTelemetry;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    partial void OnMicrophoneTelemetryChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Telemetry.MicrophoneTelemetry", value.ToString());
        TelemetrySettingsService.Instance.Current.MicrophoneTelemetry = value;
        TelemetrySettingsService.Instance.Save();
    }

    public IReadOnlyList<SettingDescriptor> Settings =>
    [
        Setting.Toggle("RecordingMicrophoneTelemetryCard",
            () => MicrophoneTelemetry,
            value => MicrophoneTelemetry = value,
            confirmOnEnable: TelemetryConsent.RequestMicrophone),
    ];

    // Called by the Diagnostics page's Reset all. Outside the sync guard, so
    // the change handler saves it like a user's flip.
    public void TurnOff() => MicrophoneTelemetry = false;

    // The module's entry in the Diagnostics registry.
    public static DiagnosticsContribution Contribution(int order) => new(
        "recording", order,
        () =>
        {
            var model = new RecordingTelemetryViewModel();
            return new DiagnosticsSource(model, model.Settings, model.Load, model.TurnOff);
        });
}
