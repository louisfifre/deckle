using Deckle.Notifications;
using Deckle.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Playground;

// Landing page of the Playground. Holds no state, no settings, no
// timers — just three routing handlers and lightweight diagnostic probes.
// Lives in NavigationCacheMode.Required
// for consistency with the other pages (cheap to keep around, avoids
// re-instantiation on every back-nav).
public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    // Route via the shell's callback registry — the page doesn't reach
    // into PlaygroundWindow directly, so the routing target can move
    // (e.g. become a Frame.Navigate in a future split). Same shape as
    // SettingsHost.OpenSetupWizard which GeneralPage uses.
    private void OnHudCardClick(object sender, RoutedEventArgs e)
    {
        PlaygroundShell.NavigateTo?.Invoke("hud");
    }

    private void OnAmbientCardClick(object sender, RoutedEventArgs e)
    {
        PlaygroundShell.NavigateTo?.Invoke("ambient");
    }

    private void OnSegmentationCardClick(object sender, RoutedEventArgs e)
    {
        PlaygroundShell.NavigateTo?.Invoke("segmentation");
    }

    // Manual probe for the notification toast channel. Fires the Playground
    // TestPrompt descriptor through the dispatcher and reports the answer.
    // The TaskCompletionSource is SET on the OS activation callback thread, but
    // this is an async-void UI handler: the await resumes on the captured
    // DispatcherQueueSynchronizationContext (the UI thread), so the line after
    // the await already runs on it. The dispatcher's internal ConfigureAwait(false)
    // does not propagate to this caller. SetNotificationOutcome's HasThreadAccess
    // guard stays as defense-in-depth — not exercised on this path. Same model
    // as InstallingPage.xaml.cs:144-147.
    private async void OnSendTestPromptClick(object sender, RoutedEventArgs e)
    {
        var dispatcher = NotificationDispatcher.Instance;
        if (dispatcher is null)
        {
            NotificationOutcomeText.Text = "dispatcher not initialized";
            return;
        }

        NotificationResponse? response;
        try
        {
            response = await dispatcher.PromptAsync(PlaygroundNotifications.TestPrompt);
        }
        catch (Exception ex)
        {
            // A channel failure rethrows after the NotificationFailed narrative.
            // This is a probe surface: report it on the outcome line instead of
            // letting the async-void escape to the App safety nets.
            SetNotificationOutcome($"failed: {ex.Message}");
            return;
        }

        // null = the notification was not shown (no channel / channel
        // unavailable) OR shown but never answered (the toast expired unseen).
        // For the enrollment-prompt semantics, ignoring is a valid answer, so
        // both are normal outcomes, not errors.
        string outcome = response is null
            ? "no answer (dropped or expired)"
            : $"{response.ActionId} | reply: {response.TextInput ?? "(none)"}";

        SetNotificationOutcome(outcome);
    }

    // Marshal the outcome onto the UI thread before touching the TextBlock.
    // "ui-update" is the sanctioned Threading operation for updating a XAML
    // control from a non-UI thread.
    private void SetNotificationOutcome(string outcome)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            NotificationOutcomeText.Text = outcome;
        }
        else
        {
            DispatcherQueue.TryEnqueueObserved(
                operation: "ui-update", caller: "playground-home",
                callback: () => NotificationOutcomeText.Text = outcome,
                rejectSource: "PLAYGROUND", rejectWhat: "notification outcome");
        }
    }
}
