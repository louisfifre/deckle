using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.Setup;

// Manual recovery: a failed row carries a "Download manually" link and, where a
// local pick can satisfy the item, an import. A blocked or filtered network
// therefore doesn't dead-end the install: the user fetches the file by hand
// and imports it, and the footer offers "Get started" without a re-run.
public sealed partial class SummaryPage : Page
{
    private SetupWindow? _setup;
    private SetupContext? _context;

    public SummaryPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is not SetupWindow setup) return;

        _setup   = setup;
        _context = setup.Context;

        setup.NextRequested += OnNextRequested;
        ApplyState();

        DeckleSetupSource.Log.SummaryShown();
        DeckleSetupSource.Log.SummaryShownDetail(_context.AllSucceeded, _context.Results.Count);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_setup is not null) _setup.NextRequested -= OnNextRequested;
    }

    // Recomputes the whole page from Results; called on entry and after each
    // manual import.
    private void ApplyState()
    {
        if (_setup is null || _context is null) return;

        bool ok = _context.AllSucceeded;

        _setup.SetStepHeader(
            Loc.Get(ok ? "Setup_StepTitle_Summary_Success"    : "Setup_StepTitle_Summary_Failure"),
            Loc.Get(ok ? "Setup_StepSubtitle_Summary_Success" : "Setup_StepSubtitle_Summary_Failure"));
        _setup.SetCurrentStep(SetupStep.Finish);
        _setup.SetBackEnabled(false);
        _setup.SetNextVisible(true);
        _setup.SetNextEnabled(true);
        _setup.SetNextLabel(Loc.Get(ok ? "Setup_NextLabel_GetStarted" : "Setup_NextLabel_Retry"));
        _setup.SetCancelVisible(!ok);

        // The same band as the steps before: after a full run it says nothing
        // is left, after a partial one what is still to fetch, and a manual
        // import brings it down through this same recompute.
        _setup.SetTotalEstimate(InstallPlan.DescribeTotal(_context));

        ResultBar.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        ResultBar.Title    = Loc.Get(ok ? "Setup_Result_CompleteTitle" : "Setup_Result_IncompleteTitle");
        ResultBar.Message  = ok
            ? Loc.Format("Setup_Result_StoredUnder_Format", _context.Location)
            : Loc.Format("Setup_Result_FailedCount_Format", CountFailed(_context), _context.Results.Count);

        // On failure, surface the always-on local diagnostics folder so a user
        // stuck in the installer, who can't reach Settings yet, can still find
        // the setup and error logs.
        OpenDiagnosticsLink.Content    = Loc.Get("Setup_OpenDiagnosticsFolder");
        OpenDiagnosticsLink.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;

        RenderResults();
    }

    private void OnNextRequested()
    {
        if (_setup is null || _context is null) return;

        if (_context.AllSucceeded)
        {
            _setup.Complete(true);
            return;
        }

        _context.Results.Clear();
        _setup.Body.Navigate(typeof(InstallingPage), _setup);
    }

    // Best-effort: a failure to open Explorer is self-evident on screen, so it
    // stays a Verbose trace rather than a Warning.
    private void OnOpenDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DiagnosticsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.DiagnosticsDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DeckleSetupSource.Log.BestEffortFailedDetail(
                "open_diagnostics", AppPaths.DiagnosticsDirectory, ex.GetType().Name, ex.Message);
        }
    }

    private void RenderResults()
    {
        if (_context is null) return;

        ItemsPanel.Children.Clear();

        foreach (var r in _context.Results)
        {
            ItemsPanel.Children.Add(BuildResultRow(r));
        }
    }

    private Grid BuildResultRow(InstallResult r)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new FontIcon
        {
            Glyph = r.Success ? Glyphs.Badge.Success : Glyphs.Cancel,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = r.DisplayName,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });

        string detail = r.Success
            ? (r.Bytes is long b
                ? Loc.Format("Setup_Result_Detail_BytesInstalled_Format", ByteSizeFormatter.Format(b))
                : Loc.Get("Setup_Result_Detail_Installed"))
            : r.ErrorMessage ?? Loc.Get("Setup_Install_UnknownError");

        stack.Children.Add(new TextBlock
        {
            Text = detail,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

        if (!r.Success)
            stack.Children.Add(BuildRecoveryRow(r));

        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        return grid;
    }

    // The item id resolves the asset URL and the import kind through the
    // catalogs: a model entry imports a single checksum-verified file, the
    // native runtime a folder of DLLs. Other items get at most the
    // manual-download link; their recovery is Retry, not a local pick.
    private FrameworkElement BuildRecoveryRow(InstallResult r)
    {
        ModelEntry? model = SpeechModels.WhisperModels.FirstOrDefault(m => m.Id == r.ItemId);
        bool isNativeRuntime = r.ItemId == InstallPlan.NativeRuntimeItemId;
        string? url = model?.Url ?? r.ItemId switch
        {
            InstallPlan.NativeRuntimeItemId => NativeRuntime.CurrentBundle.Url,
            InstallPlan.SileroItemId        => Deckle.Vad.SileroVadModel.Url,
            InstallPlan.AnytypeItemId       => Deckle.Anytype.BackendInstallation.CurrentBundle.Url,
            _ => null,
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
        };

        if (!string.IsNullOrWhiteSpace(url))
        {
            row.Children.Add(new HyperlinkButton
            {
                Content = Loc.Get("Setup_DownloadManually"),
                NavigateUri = new Uri(url),
            });
        }

        if (model is not null || isNativeRuntime)
        {
            var importButton = new Button
            {
                Content = Loc.Get(model is not null ? "Setup_ImportFile" : "Setup_ImportFolder"),
            };
            var importStatus = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
            };
            importButton.Click += async (_, _) => await OnImportAsync(r, model, importButton, importStatus);

            row.Children.Add(importButton);
            row.Children.Add(importStatus);
        }

        return row;
    }

    private async Task OnImportAsync(
        InstallResult r,
        ModelEntry? model,
        Button button,
        TextBlock status)
    {
        if (_setup is null || _context is null) return;

        button.IsEnabled = false;
        try
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_setup);

            if (model is not null)
            {
                var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
                picker.FileTypeFilter.Add(Path.GetExtension(model.FileName));
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file is null) { button.IsEnabled = true; return; }

                status.Text = Loc.Get("Setup_Importing");

                string dest = Path.Combine(AppPaths.ModelsDirectory, model.FileName);
                string src = file.Path;
                string? expected = model.Sha256;

                bool ok = await Task.Run(() =>
                {
                    Directory.CreateDirectory(AppPaths.ModelsDirectory);
                    File.Copy(src, dest, overwrite: true);
                    if (string.IsNullOrWhiteSpace(expected)) return true;
                    if (!string.Equals(ComputeSha256(dest), expected, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(dest);
                        return false;
                    }
                    return true;
                });

                if (!ok)
                {
                    DeckleSetupSource.Log.ItemImportFailed(r.ItemId, "sha256_mismatch", "", "");
                    status.Text = Loc.Get("Setup_ImportChecksumMismatch");
                    button.IsEnabled = true;
                    return;
                }

                long importedBytes = new FileInfo(dest).Length;
                DeckleSetupSource.Log.ItemImported(r.ItemId, importedBytes);
                MarkImported(r.ItemId, r.DisplayName, importedBytes);
            }
            else
            {
                var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
                picker.FileTypeFilter.Add("*"); // required by the API for FolderPicker
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var folder = await picker.PickSingleFolderAsync();
                if (folder is null) { button.IsEnabled = true; return; }

                status.Text = Loc.Get("Setup_Importing");
                await Task.Run(() => NativeRuntime.CopyFromFolder(folder.Path));

                if (!NativeRuntime.IsInstalled())
                {
                    string missing = string.Join(", ", NativeRuntime.GetMissing());
                    DeckleSetupSource.Log.ItemImportFailed(r.ItemId, "files_missing", "", missing);
                    status.Text = Loc.Format("Setup_ImportFailed_Format", missing);
                    button.IsEnabled = true;
                    return;
                }

                // CopyFromFolder load-checked the complete copy: a folder from
                // another build can hold every name and still not load here.
                if (!NativeRuntime.IsUsable())
                {
                    string summary = NativeRuntime.CheckLoad().FirstFailure?.Summary ?? "";
                    DeckleSetupSource.Log.ItemImportFailed(r.ItemId, "load_check_failed", "", summary);
                    status.Text = Loc.Format("Setup_ImportFailed_Format", summary);
                    button.IsEnabled = true;
                    return;
                }

                DeckleSetupSource.Log.ItemImported(r.ItemId, 0);
                MarkImported(r.ItemId, r.DisplayName, null);
            }

            ApplyState();
        }
        catch (Exception ex)
        {
            DeckleSetupSource.Log.ItemImportFailed(r.ItemId, "threw", ex.GetType().Name, ex.Message);
            status.Text = Loc.Format("Setup_ImportFailed_Format", ex.Message);
            button.IsEnabled = true;
        }
    }

    private void MarkImported(string itemId, string displayName, long? bytes)
    {
        if (_context is null) return;

        for (int i = 0; i < _context.Results.Count; i++)
        {
            if (_context.Results[i].ItemId == itemId)
            {
                _context.Results[i] = new InstallResult(itemId, displayName, true, null, bytes);
                return;
            }
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex)
        {
            // Best-effort: the next import overwrites the rejected copy anyway.
            DeckleSetupSource.Log.BestEffortFailedDetail("delete_rejected_import", path, ex.GetType().Name, ex.Message);
        }
    }

    private static int CountFailed(SetupContext ctx)
    {
        int n = 0;
        foreach (var r in ctx.Results) if (!r.Success) n++;
        return n;
    }

}
