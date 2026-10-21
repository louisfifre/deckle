using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Deckle.Catalog;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.Setup;

// The Dictation step: collects the speech choices up-front so the install step
// can run unattended. Browse copies the whisper DLLs immediately
// (NativeRuntime.CopyFromFolder), so a picked runtime leaves nothing native for
// the install step to do.
public sealed partial class ChoicesPage : Page
{
    private SetupWindow? _setup;
    private SetupContext? _context;

    public ChoicesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is not SetupWindow setup) return;

        _setup   = setup;
        _context = setup.Context;

        setup.SetStepHeader(
            Loc.Get("Setup_StepTitle_Choices"),
            Loc.Get("Setup_StepSubtitle_Choices"));
        setup.SetBackEnabled(true);
        setup.SetNextLabel(Loc.Get("Setup_NextLabel_Install"));
        setup.SetNextVisible(true);
        setup.SetCancelVisible(true);
        setup.NextRequested += OnNextRequested;
        setup.BackRequested += OnBackRequested;

        LocationPathText.Text = _context.Location;
        PopulateModelRadio();
        RefreshNativeStatus();
        UpdateTotalEstimate();
        UpdateNextEnabled();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_setup is null) return;
        _setup.NextRequested -= OnNextRequested;
        _setup.BackRequested -= OnBackRequested;
    }

    private void OnBackRequested()
    {
        if (_setup is null) return;
        if (_setup.Body.CanGoBack) _setup.Body.GoBack();
    }

    // ── Speech runtime ────────────────────────────────────────────────────────

    private async void OnBrowseNativeClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_setup is null) return;

        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeFilter.Add("*"); // required by the API for FolderPicker

            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_setup);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;

            int copied = NativeRuntime.CopyFromFolder(folder.Path);
            DeckleSetupSource.Log.NativeSourcePicked();
            DeckleSetupSource.Log.NativeSourcePickedDetail(folder.Path, copied);

            RefreshNativeStatus();
            UpdateNextEnabled();
        }
        catch (Exception ex)
        {
            DeckleSetupSource.Log.NativeImportFailed(ex.GetType().Name, ex.Message);
            NativeStatusText.Text = Loc.Format("Setup_Native_ImportFailed_Format", ex.Message);
        }
    }

    private void RefreshNativeStatus()
    {
        if (NativeRuntime.IsInstalled())
        {
            NativeStatusText.Text = Loc.Get("Setup_Native_Installed");
            BrowseNativeButton.Content = Loc.Get("Setup_Native_Replace");
        }
        else if (NativeRuntime.BundleUrlIsPlaceholder)
        {
            int missing = NativeRuntime.GetMissing().Count;
            NativeStatusText.Text = Loc.Format("Setup_Native_Missing_Format", missing);
            BrowseNativeButton.Content = Loc.Get("Common_Browse");
        }
        else
        {
            NativeStatusText.Text = Loc.Format(
                "Setup_Native_WillDownload_Format",
                ByteSizeFormatter.Format(NativeRuntime.CurrentBundle.SizeBytes));
            BrowseNativeButton.Content = Loc.Get("Setup_Native_BrowseLocal");
        }
    }

    // ── Speech model ──────────────────────────────────────────────────────────

    private void PopulateModelRadio()
    {
        ModelRadio.ItemsSource = SpeechModels.WhisperModels;

        int defaultIndex = 0;
        for (int i = 0; i < SpeechModels.WhisperModels.Count; i++)
        {
            ModelEntry entry = SpeechModels.WhisperModels[i];
            if (entry.FileName == SpeechModels.DefaultModelFileName) defaultIndex = i;
        }

        ModelRadio.SelectedIndex = defaultIndex;
    }

    private void OnModelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_context is null) return;

        if (ModelRadio.SelectedItem is ModelEntry entry)
            _context.SelectedModel = entry;

        UpdateTotalEstimate();
        UpdateNextEnabled();
    }

    // ── Estimate + Next gate ──────────────────────────────────────────────────

    private void UpdateTotalEstimate()
    {
        if (_context is null) return;

        // The same plan InstallingPage runs, so the estimate matches what the
        // install step fetches; the model radio feeds it through SelectedModel.
        long pendingBytes = InstallPlan.PendingBytes(_context);

        TotalEstimateBar.Message = pendingBytes > 0
            ? Loc.Format("Setup_TotalEstimate_Pending_Format", ByteSizeFormatter.Format(pendingBytes))
            : Loc.Get("Setup_TotalEstimate_NothingPending");
    }

    private void UpdateNextEnabled()
    {
        if (_setup is null) return;
        // With a bundle URL the install step downloads a missing runtime; with
        // a placeholder URL only a manual Browse can supply it.
        bool nativeReady = NativeRuntime.IsInstalled() || !NativeRuntime.BundleUrlIsPlaceholder;
        bool ready = nativeReady && ModelRadio.SelectedIndex >= 0;
        _setup.SetNextEnabled(ready);
    }

    // ── Next ──────────────────────────────────────────────────────────────────

    private void OnNextRequested()
    {
        if (_setup is null || _context is null) return;
        _context.ChoicesConfirmed = true;
        DeckleSetupSource.Log.ChoicesConfirmed();
        DeckleSetupSource.Log.ChoicesConfirmedDetail(_context.Location, _context.SelectedModel!.Id);

        // Install mode: provisioning cannot run in this temp process (AppPaths
        // froze on the default data root), so Install means Deploy — place the
        // binaries and relaunch from the install folder; the installed process
        // runs the provisioning step with the right paths.
        _setup.Body.Navigate(
            _context.InstallMode ? typeof(DeployPage) : typeof(InstallingPage),
            _setup);
    }
}
