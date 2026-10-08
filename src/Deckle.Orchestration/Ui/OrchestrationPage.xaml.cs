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
        NothingNew.Visibility = Visibility.Collapsed;
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

    // Scan Steam and the chosen folders. The button greys while the scan runs
    // so a second click cannot start a parallel one; the outcome is the list
    // itself, or the "Nothing new found" line when nothing was added.
    private async void OnScanClick(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        NothingNew.Visibility = Visibility.Collapsed;
        DuplicateInfo.IsOpen = false;
        try
        {
            ScanResult result = await ViewModel.ScanAsync();
            NothingNew.Visibility = result.Added == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ReportFailure("scan", ex);
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
            };
            PickFolderResult? result = await picker.PickSingleFolderAsync();
            if (result?.Path is { } path)
            {
                ViewModel.AddFolder(path); // a folder already listed is ignored
                FoldersExpander.IsExpanded = true;
            }
        }
        catch (Exception ex)
        {
            ReportFailure("add_scan_folder", ex);
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
