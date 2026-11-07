using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Catalog;
using Deckle.Diagnostics;

namespace Deckle.Settings;

// ── Developer mode: Capture the interface ────────────────────────────────────
//
// The card that starts an interface capture: every open Deckle window, then
// every settings page in turn, to PNG files in a dated folder with an
// index.json (the App runs it through SettingsHost.CaptureInterface, the
// capture itself lives in Deckle.Vision, InterfaceCaptureRun). The card shows
// while Developer mode is on and the App wired the capture; a setting that does
// not apply is hidden.
//
// While the capture runs, the window walks through the settings pages and comes
// back here; the button is greyed and the ring turns, the one case where
// greying is right, an action in progress. The outcome stays in the InfoBar
// below the card with a way to the folder, until closed.
public sealed partial class GeneralPage
{
    private string? _lastCaptureFolder;

    private void WireInterfaceCapture()
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChangedForCapture;
        RefreshCaptureCard();
    }

    private void OnViewModelPropertyChangedForCapture(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GeneralViewModel.DeveloperMode))
            RefreshCaptureCard();
    }

    private void RefreshCaptureCard()
    {
        bool available = ViewModel.DeveloperMode && SettingsHost.CaptureInterface is not null;
        CaptureInterfaceCard.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        if (!available) CaptureInterfaceInfoBar.IsOpen = false;
    }

    private async void CaptureInterfaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsHost.CaptureInterface is not { } capture) return;

        CaptureInterfaceInfoBar.IsOpen = false;
        CaptureInterfaceButton.IsEnabled = false;
        CaptureInterfaceRing.Visibility = Visibility.Visible;
        CaptureInterfaceRing.IsActive = true;
        try
        {
            InterfaceCaptureOutcome? outcome = await capture();
            if (outcome is not null) ShowCaptureOutcome(outcome);
        }
        catch (Exception)
        {
            // The App's hook logs what stopped the run as the run's own end and
            // returns; reaching here means it threw before the run began, which
            // the App logged too. The page only says the capture did not happen,
            // since an exception out of an async void handler would end the app.
            _lastCaptureFolder = null;
            ShowCaptureMessage(InfoBarSeverity.Error, Text("GeneralCaptureInterface_Failed"));
        }
        finally
        {
            CaptureInterfaceRing.IsActive = false;
            CaptureInterfaceRing.Visibility = Visibility.Collapsed;
            CaptureInterfaceButton.IsEnabled = true;
        }
    }

    private void ShowCaptureOutcome(InterfaceCaptureOutcome outcome)
    {
        _lastCaptureFolder = outcome.Captured > 0 ? outcome.Folder : null;

        if (outcome.Captured == 0)
            ShowCaptureMessage(InfoBarSeverity.Error, Text("GeneralCaptureInterface_Failed"));
        else if (outcome.Failed > 0)
            ShowCaptureMessage(InfoBarSeverity.Warning,
                Text("GeneralCaptureInterface_Partial_Format", outcome.Captured, outcome.Failed));
        else
            ShowCaptureMessage(InfoBarSeverity.Success,
                Text("GeneralCaptureInterface_Done_Format", outcome.Captured));
    }

    // From this module's own .resw: Loc.Get reads the App's, which does not
    // mirror these strings.
    private static string Text(string key, params object[] args)
        => string.Format(System.Globalization.CultureInfo.CurrentCulture,
            Loc.GetFrom("Deckle.Settings", key), args);

    private void ShowCaptureMessage(InfoBarSeverity severity, string message)
    {
        CaptureInterfaceInfoBar.Severity = severity;
        CaptureInterfaceInfoBar.Message = message;
        OpenCaptureFolderButton.Visibility = _lastCaptureFolder is null ? Visibility.Collapsed : Visibility.Visible;
        CaptureInterfaceInfoBar.IsOpen = true;
    }

    private void OpenCaptureFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastCaptureFolder is null) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _lastCaptureFolder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DeckleSettingsUxSource.Log.FolderActionFailed("open", ex.GetType().Name, ex.HResult, ex.Message);
        }
    }
}
