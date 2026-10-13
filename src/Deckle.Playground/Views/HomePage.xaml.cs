using Deckle.Notifications;
using Deckle.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Playground;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

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

    private void OnCorrectionCardClick(object sender, RoutedEventArgs e)
    {
        PlaygroundShell.NavigateTo?.Invoke("correction");
    }

    // The answer completes on the OS activation callback thread, but this
    // async-void UI handler resumes on the captured UI synchronization
    // context; the dispatcher's internal ConfigureAwait(false) does not reach
    // this caller. SetNotificationOutcome's thread check is only a backstop.
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
            // A channel failure rethrows; a probe reports it on the outcome
            // line rather than letting the async-void reach the App safety nets.
            SetNotificationOutcome($"failed: {ex.Message}");
            return;
        }

        // null means not shown (no channel available) or never answered (the
        // toast expired). Ignoring a prompt is a valid answer, not an error.
        string outcome = response is null
            ? "no answer (dropped or expired)"
            : $"{response.ActionId} | reply: {response.TextInput ?? "(none)"}";

        SetNotificationOutcome(outcome);
    }

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
