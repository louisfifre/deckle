using Microsoft.UI.Xaml;
using Deckle.Diagnostics;
using Deckle.Shell;

namespace Deckle.Settings;

// Windows shows no toast from a process that runs as administrator. The
// notification channel opens the toasts_elevated user incident when it finds
// itself elevated, and the tray's line for it opens this page. The bar mirrors
// that incident: open while it is open.
//
// The registry outlives every page, so the page listens only while it is in
// the tree: a closed Settings window leaves no handler behind.
public sealed partial class GeneralPage
{
    private void WireToastsElevatedInfoBar()
    {
        Loaded += (_, _) =>
        {
            UserIncidentRegistry.Instance.Changed += OnUserIncidentsChanged;
            RefreshToastsElevatedInfoBar();
        };
        Unloaded += (_, _) => UserIncidentRegistry.Instance.Changed -= OnUserIncidentsChanged;
    }

    // Raised on the thread that opened or closed an incident.
    private void OnUserIncidentsChanged()
        => DispatcherQueue.TryEnqueueOrLog(
            RefreshToastsElevatedInfoBar, "SETTINGS", "elevated notifications bar");

    private void RefreshToastsElevatedInfoBar()
        => ToastsElevatedInfoBar.IsOpen =
            UserIncidentRegistry.Instance.IsOpen(UserIncidents.ToastsElevated);
}
