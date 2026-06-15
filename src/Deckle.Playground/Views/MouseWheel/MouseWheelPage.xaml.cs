using System;
using System.Diagnostics;
using Deckle.Core;
using Deckle.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Playground;

// Wheel-capture surface — Palier 0 of the wheel→touchpad work. A single
// diagnostic toggle drives MouseWheelSettings.RecordEvents; the App's
// ReconcileMouseWheel turns that into a JSONL capture on the shared input
// host, so flipping it here starts/stops recording globally (it survives
// closing the Playground). Plus a shortcut to the telemetry folder where the
// wheel-events-*.jsonl files land. No heavy resources — nothing to dispose.
public sealed partial class MouseWheelPage : Page
{
    public MouseWheelPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        // Seed from the persisted intent. This fires Toggled (the handler is
        // already wired by InitializeComponent), but the equality guard there
        // makes that a no-op — no spurious save on construction.
        RecordToggle.IsOn = MouseWheelSettingsService.Instance.Current.RecordEvents;
        FolderPathText.Text = AppPaths.TelemetryDirectory;
    }

    // Persist the intent; Save raises Changed, which App.ReconcileMouseWheel
    // turns into a start/stop on the shared input host. The guard skips the
    // redundant write the constructor's seed assignment would otherwise cause.
    private void OnRecordToggled(object sender, RoutedEventArgs e)
    {
        var settings = MouseWheelSettingsService.Instance.Current;
        if (settings.RecordEvents == RecordToggle.IsOn) return;
        settings.RecordEvents = RecordToggle.IsOn;
        MouseWheelSettingsService.Instance.Save();
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        // Dev-surface shortcut; UseShellExecute hands the path to Explorer.
        // Best-effort — a failure here is not worth a narrative.
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.TelemetryDirectory,
                UseShellExecute = true,
            });
        }
        catch
        {
            // swallowed by design — opening a folder is not a failure path
        }
    }
}
