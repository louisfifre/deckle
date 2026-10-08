using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;

namespace Deckle.Orchestration;

// "Apps and games" settings page, resolved by the Settings NavigationView via
// the tag in OrchestrationSettingsModule.PageTag. Code-behind owns what needs
// the window: the file picker, the running-app dialog, the empty-state swap and
// the duplicate notice. Picker and dialog failures are logged, never thrown.
public sealed partial class OrchestrationPage : Page
{
    public OrchestrationViewModel ViewModel { get; } = new();

    public OrchestrationPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        ViewModel.Entries.CollectionChanged += OnEntriesChanged;
        RefreshEmptyState();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DuplicateInfo.IsOpen = false;
        ViewModel.Load();
        RefreshEmptyState();
    }

    private async void OnAddAppClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string? path = await PickExecutableAsync();
            if (path is null) return;
            ShowDuplicate(!ViewModel.AddExecutable(path));
        }
        catch (Exception ex)
        {
            ReportFailure("add_app", ex);
        }
    }

    private async void OnAddRunningClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new RunningProcessDialog(ViewModel.IsListed) { XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (dialog.Selection is { } app)
                ShowDuplicate(!ViewModel.AddRunning(app));
        }
        catch (Exception ex)
        {
            ReportFailure("add_running_app", ex);
        }
    }

    private async void OnChangeExecutableClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AppEntryRow row) return;
        try
        {
            string? path = await PickExecutableAsync();
            if (path is null) return;
            ShowDuplicate(!ViewModel.ChangeExecutable(row, path));
        }
        catch (Exception ex)
        {
            ReportFailure("change_executable", ex);
        }
    }

    // The Windows App SDK picker takes the hosting window's id; the page reads
    // it from its own XamlRoot, so it needs nothing from the Settings shell.
    private async Task<string?> PickExecutableAsync()
    {
        var picker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add(".exe");
        PickFileResult? result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    private void ShowDuplicate(bool duplicate) => DuplicateInfo.IsOpen = duplicate;

    private static void ReportFailure(string action, Exception ex)
    {
        DeckleOrchestrationSource.Log.PageActionFailed();
        DeckleOrchestrationSource.Log.PageActionFailedDetail(action, ex.GetType().Name, ex.Message);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshEmptyState();

    private void RefreshEmptyState()
    {
        bool any = ViewModel.Entries.Count > 0;
        EntriesList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }
}
