using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Orchestration;

// "Add running app" picker. Reads the running apps once, off the UI thread,
// and hides those already in the registry. The caller reads Selection after
// a Primary result.
public sealed partial class RunningProcessDialog : ContentDialog
{
    private readonly Func<AppEntry, bool> _isListed;

    public RunningApp? Selection => AppsList.SelectedItem as RunningApp;

    public RunningProcessDialog(Func<AppEntry, bool> isListed)
    {
        _isListed = isListed;
        InitializeComponent();
        Opened += OnOpened;
    }

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        IReadOnlyList<RunningApp> apps;
        try
        {
            apps = await Task.Run(RunningApps.Enumerate);
        }
        catch (Exception ex)
        {
            DeckleOrchestrationSource.Log.PageActionFailed(
                "list_running_apps", ex.GetType().Name, ex.Message);
            apps = [];
        }

        var offered = apps
            .Where(app => !_isListed(new AppEntry
            {
                ExecutablePath = app.ExecutablePath,
                ProcessName = app.ProcessName,
            }))
            .ToList();

        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        AppsList.ItemsSource = offered;
        AppsList.Visibility = offered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = offered.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        IsPrimaryButtonEnabled = AppsList.SelectedItem is not null;
}
