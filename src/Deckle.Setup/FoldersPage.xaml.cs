using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Deckle.Catalog;
using Deckle.Modules;

namespace Deckle.Setup;

// ── FoldersPage ──────────────────────────────────────────────────────────────
//
// Install-mode only — the step between the module selector and the model
// choice. Collects the two folders the installer doctrine revolves around:
// the application folder (binaries, per user) and the data folder (models,
// settings, logs — the heavy assets, relocatable off a saturated C: via
// DECKLE_DATA_ROOT).
//
// The space recap is computed per drive: the app drive owes the payload's
// size (the extracted tree this temp process runs from), the data drive owes
// the install plan's pending downloads — both land on the same drive line
// when the folders share one. A drive that can't take its share flips the
// InfoBar to Error and gates Next.
public sealed partial class FoldersPage : Page
{
    private SetupWindow? _setup;
    private SetupContext? _context;

    // The payload's on-disk size, walked once per navigation — the source
    // tree doesn't change while the wizard is up.
    private long _payloadBytes;

    public FoldersPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is not SetupWindow setup) return;

        _setup   = setup;
        _context = setup.Context;

        setup.SetStepHeader(
            Loc.Get("Setup_StepTitle_Folders"),
            Loc.Get("Setup_StepSubtitle_Folders"));
        setup.SetBackEnabled(true);
        setup.SetNextLabel(Loc.Get("Setup_NextLabel_Continue"));
        setup.SetNextVisible(true);
        setup.SetCancelVisible(true);
        setup.NextRequested += OnNextRequested;
        setup.BackRequested += OnBackRequested;

        BrowseAppButton.Content  = Loc.Get("Common_Browse");
        BrowseDataButton.Content = Loc.Get("Common_Browse");

        RefreshPaths();
        setup.SetNextEnabled(false);
        _ = MeasurePayloadAndRefreshAsync(_context.SourceDirectory);
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

    // ── Browsing ──────────────────────────────────────────────────────────────

    private async void OnBrowseAppClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        string? picked = await PickFolderAsync();
        if (picked is null || _context is null) return;
        _context.InstallDirectory = picked;
        RefreshPaths();
        RefreshSpace();
    }

    private async void OnBrowseDataClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        string? picked = await PickFolderAsync();
        if (picked is null || _context is null) return;
        _context.DataDirectory = picked;
        RefreshPaths();
        RefreshSpace();
    }

    private async System.Threading.Tasks.Task<string?> PickFolderAsync()
    {
        if (_setup is null) return null;
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*"); // required by the API for FolderPicker

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_setup);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private void RefreshPaths()
    {
        if (_context is null) return;
        AppPathText.Text  = _context.InstallDirectory;
        DataPathText.Text = _context.DataDirectory;
    }

    // ── Space recap + Next gate ───────────────────────────────────────────────

    private async Task MeasurePayloadAndRefreshAsync(string sourceDirectory)
    {
        long payloadBytes = await Task.Run(() => DirectorySize(sourceDirectory));
        if (!ReferenceEquals(_setup?.Body.Content, this))
            return;

        _payloadBytes = payloadBytes;
        RefreshSpace();
    }

    private void RefreshSpace()
    {
        if (_setup is null || _context is null) return;

        // Per-drive requirement: payload on the app drive, pending downloads
        // on the data drive, summed when both folders share a drive.
        var needed = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        void Add(string dir, long bytes)
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(dir));
            if (root is null) return;
            needed[root] = needed.TryGetValue(root, out long prior) ? prior + bytes : bytes;
        }
        Add(_context.InstallDirectory, _payloadBytes);
        Add(_context.DataDirectory, InstallPlan.PendingBytes(_context));

        bool allFit = true;
        var lines = new List<string>();
        foreach (var (root, bytes) in needed)
        {
            long free;
            try { free = new DriveInfo(root).AvailableFreeSpace; }
            catch (Exception) { free = -1; } // unreadable drive — flag, don't crash
            bool fits = free >= bytes;
            allFit &= fits;
            lines.Add(Loc.Format("Setup_Folders_DriveLine_Format",
                root, ByteSizeFormatter.Format(bytes),
                free >= 0 ? ByteSizeFormatter.Format(free) : Loc.Get("Setup_Folders_DriveUnknown")));
        }

        SpaceBar.Message  = string.Join("\n", lines);
        SpaceBar.Severity = allFit ? InfoBarSeverity.Informational : InfoBarSeverity.Error;
        _setup.SetNextEnabled(allFit);
    }

    private static long DirectorySize(string dir)
    {
        long total = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                total += new FileInfo(file).Length;
        }
        catch (Exception) { /* a partial walk still beats no estimate */ }
        return total;
    }

    // ── Next ──────────────────────────────────────────────────────────────────

    private void OnNextRequested()
    {
        if (_setup is null || _context is null) return;

        // The data folder is now the wizard's declared location — the Choices
        // recap displays Context.Location, which must be the chosen root, not
        // the default AppPaths froze on in this temp process.
        _context.Location = _context.DataDirectory;

        DeckleSetupSource.Log.FoldersChosen();
        DeckleSetupSource.Log.FoldersChosenDetail(_context.InstallDirectory, _context.DataDirectory);

        _setup.Body.Navigate(
            _context.SelectedModules.Contains(ModuleIds.Transcription)
                ? typeof(ChoicesPage)
                : typeof(DeployPage),
            _setup);
    }
}
