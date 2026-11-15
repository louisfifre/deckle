using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Deckle.Projects.Views;

// Refresh (spec, Window: one command, F5) and the window coming back to the
// front. Both ask the page shown to read again what it shows; the readouts and
// the cards' meta lines are read again "when the screen opens, when the window
// comes back to the front, on Refresh, and when an action ends" (spec,
// Behaviours). The screen opening is each page's own first read, and an action
// ending is the workspace's, so these two are what is left for the window.
public sealed partial class ProjectsWindow
{
    // The first activation is the window opening: the page it shows has just
    // read. Only an activation that follows a deactivation is "back to the
    // front".
    private bool _deactivated;

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _deactivated = true;
            return;
        }
        if (!_deactivated) return;
        _deactivated = false;
        _ = RefreshPageAsync(RefreshReason.WindowActivated);
    }

    private async Task RefreshPageAsync(RefreshReason reason)
    {
        if (_page is not { } page) return;
        try
        {
            await page.RefreshAsync(reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A refresh that fails leaves the page as it was; the log says what
            // kind of failure it was.
            DeckleProjectsSource.Log.ProjectsPageFailed(KindOf(_history.Current!), ex.GetType().Name);
        }
    }

    private void OnRefreshAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _ = RefreshPageAsync(RefreshReason.Requested);
        args.Handled = true;
    }

    // Alt+Left, as in File Explorer and a browser. It does nothing on Home,
    // where the title bar has no back button either.
    private void OnBackAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_history.CanGoBack || _history.Current is ProjectsTarget.Home) return;
        GoBack();
        args.Handled = true;
    }
}
