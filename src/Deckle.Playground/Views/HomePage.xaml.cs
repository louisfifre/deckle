using Deckle.Notifications;
using Deckle.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Playground;

// Landing page of the Playground. Holds no state, no settings, no
// timers — just two routing handlers. Lives in NavigationCacheMode.Required
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

    // Manual probe for the notification toast channel. Fires the Playground
    // TestPrompt descriptor through the dispatcher and reports the answer.
    // PromptAsync completes on a background thread (the channel awaits the OS
    // toast callback), so the outcome TextBlock is updated only after marshalling
    // back to the UI thread — touching a XAML control off-thread throws
    // COMException (RPC_E_WRONG_THREAD).
    private async void OnSendTestPromptClick(object sender, RoutedEventArgs e)
    {
        var dispatcher = NotificationDispatcher.Instance;
        if (dispatcher is null)
        {
            NotificationOutcomeText.Text = "dispatcher not initialized";
            return;
        }

        NotificationResponse? response = await dispatcher.PromptAsync(
            PlaygroundNotifications.TestPrompt);

        // null = the notification could not be shown (no channel / channel
        // unavailable). For the enrollment-prompt semantics, ignoring is a
        // valid answer, so a dropped prompt is a normal outcome, not an error.
        string outcome = response is null
            ? "dropped (no channel available)"
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
