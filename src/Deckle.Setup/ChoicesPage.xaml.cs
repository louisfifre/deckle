using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Deckle.Catalog;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.Setup;

// ── ChoicesPage ──────────────────────────────────────────────────────────────
//
// First step of the wizard. Collects every user choice up-front so the
// install step can run unattended afterwards:
//
//   • Install location — read-only display in V1 (default UserDataRoot).
//     Custom location lands in a later iteration.
//   • Speech runtime  — Browse for a folder containing the whisper DLLs.
//     Copy is immediate (NativeRuntime.CopyFromFolder), so the install
//     step has nothing native left to do.
//   • Speech model    — radio between the catalog's Whisper models.
//
// The Install button is enabled only when both the runtime is installed
// and a model is selected. Clicking Install navigates to InstallingPage,
// which downloads the chosen model.
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
        // The module selector precedes this page — Back returns to it.
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
            DeckleSetupSource.Log.NativeImportFailed();
            DeckleSetupSource.Log.NativeImportFailedDetail($"{ex.GetType().Name}: {ex.Message}");
            NativeStatusText.Text = Loc.Format("Setup_Native_ImportFailed_Format", ex.Message);
        }
    }

    private void RefreshNativeStatus()
    {
        if (NativeRuntime.IsInstalled())
        {
            // Already installed — local copy from a previous run, setup-assets.ps1,
            // or a prior Browse... pass. The button lets the user replace it
            // (e.g. after a manual rebuild of whisper.cpp).
            NativeStatusText.Text = Loc.Get("Setup_Native_Installed");
            BrowseNativeButton.Content = Loc.Get("Setup_Native_Replace");
        }
        else if (NativeRuntime.BundleUrlIsPlaceholder)
        {
            // Auto-download disabled for this build. Browse... is the only
            // path forward — surface it as a primary action.
            int missing = NativeRuntime.GetMissing().Count;
            NativeStatusText.Text = Loc.Format("Setup_Native_Missing_Format", missing);
            BrowseNativeButton.Content = Loc.Get("Common_Browse");
        }
        else
        {
            // Auto-download available — surface the size so the user understands
            // what's about to happen. Browse... stays as a secondary "I have it
            // locally already" affordance.
            NativeStatusText.Text = Loc.Format(
                "Setup_Native_WillDownload_Format",
                FormatBytes(NativeRuntime.CurrentBundle.SizeBytes));
            BrowseNativeButton.Content = Loc.Get("Setup_Native_BrowseLocal");
        }
    }

    // ── Speech model ──────────────────────────────────────────────────────────

    private void PopulateModelRadio()
    {
        ModelRadio.Items.Clear();

        int defaultIndex = 0;
        for (int i = 0; i < SpeechModels.WhisperModels.Count; i++)
        {
            var entry = SpeechModels.WhisperModels[i];
            ModelRadio.Items.Add(new RadioButton
            {
                Content = entry.DisplayName,
                Tag     = entry.Id,
            });
            if (entry.FileName == SpeechModels.DefaultModelFileName) defaultIndex = i;
        }

        ModelRadio.SelectedIndex = defaultIndex;
    }

    private void OnModelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_context is null) return;

        if (ModelRadio.SelectedItem is RadioButton rb && rb.Tag is string id)
        {
            foreach (var entry in SpeechModels.WhisperModels)
            {
                if (entry.Id == id)
                {
                    _context.SelectedModel = entry;
                    break;
                }
            }
        }

        UpdateTotalEstimate();
        UpdateNextEnabled();
    }

    // ── Estimate + Next gate ──────────────────────────────────────────────────

    private void UpdateTotalEstimate()
    {
        if (_context is null) return;

        // The consolidated total: everything the install step will actually
        // fetch for the selected modules — the same plan InstallingPage runs,
        // summed over the items not yet on disk. The model radio feeds the
        // plan through SelectedModel, so a model swap re-totals live.
        long pendingBytes = InstallPlan.PendingBytes(_context);

        TotalEstimateBar.Message = pendingBytes > 0
            ? Loc.Format("Setup_TotalEstimate_Pending_Format", FormatBytes(pendingBytes))
            : Loc.Get("Setup_TotalEstimate_NothingPending");
    }

    private void UpdateNextEnabled()
    {
        if (_setup is null) return;
        // Native runtime gate is conditional on the bundle URL: when auto-DL
        // is wired, the install page handles missing runtime via download.
        // When the URL is still a placeholder, only a manual Browse... can
        // unblock the wizard, so we keep the legacy gate.
        bool nativeReady = NativeRuntime.IsInstalled() || !NativeRuntime.BundleUrlIsPlaceholder;
        bool ready = nativeReady && ModelRadio.SelectedIndex >= 0;
        _setup.SetNextEnabled(ready);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)               return $"{bytes} B";
        if (bytes < 1024L * 1024)       return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F0} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F1} GB";
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
