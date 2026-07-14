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

// ── SummaryPage ──────────────────────────────────────────────────────────────
//
// Final wizard step. Renders the per-item Results captured by InstallingPage,
// surfaces success or failure as an InfoBar at the top, and configures the
// shell footer to either complete the wizard ("Get started") or offer a
// retry on the install step ("Retry" + "Quit").
//
// Retry path navigates back to InstallingPage with a fresh Results list —
// the user gets another shot at the failed download(s) without re-doing
// the choices step.
//
// Manual recovery: a failed row also carries a "Download manually" link to
// the asset URL and an import affordance (a folder for the native runtime, a
// file for a model). A blocked or filtered network — the download server the
// installer can't reach — therefore doesn't dead-end the first run: the user
// fetches the file by hand and imports it. A successful import flips the row
// to done in place and, once every row succeeds, the footer offers "Get
// started" without a re-run.
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

    // Header, footer, result InfoBar and rows — recomputed from the current
    // Results. Called on entry and again after a manual import changes a row,
    // so the whole page reflects the new success/failure balance in one place.
    private void ApplyState()
    {
        if (_setup is null || _context is null) return;

        bool ok = _context.AllSucceeded;

        _setup.SetStepHeader(
            Loc.Get(ok ? "Setup_StepTitle_Summary_Success"    : "Setup_StepTitle_Summary_Failure"),
            Loc.Get(ok ? "Setup_StepSubtitle_Summary_Success" : "Setup_StepSubtitle_Summary_Failure"));
        _setup.SetBackEnabled(false);
        _setup.SetNextVisible(true);
        _setup.SetNextEnabled(true);
        _setup.SetNextLabel(Loc.Get(ok ? "Setup_NextLabel_GetStarted" : "Setup_NextLabel_Retry"));
        _setup.SetCancelVisible(!ok);

        ResultBar.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        ResultBar.Title    = Loc.Get(ok ? "Setup_Result_CompleteTitle" : "Setup_Result_IncompleteTitle");
        ResultBar.Message  = ok
            ? Loc.Format("Setup_Result_StoredUnder_Format", _context.Location)
            : Loc.Format("Setup_Result_FailedCount_Format", CountFailed(_context), _context.Results.Count);

        // On failure, surface the always-on local diagnostics folder so a user
        // stuck at first run — who can't reach Settings yet — can still find
        // the setup/error logs to report the problem. Hidden on success.
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

        // Retry: clear previous results and re-enter the install step.
        _context.Results.Clear();
        _setup.Body.Navigate(typeof(InstallingPage), _setup);
    }

    // Opens the always-on local diagnostics folder (setup + error logs) in
    // Explorer. Best-effort: opening Explorer is a trivial user action and its
    // failure is self-evident on screen, so it is swallowed — the Setup
    // provider has no folder-open event and its frozen id set must not grow
    // for this.
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
        catch
        {
            // see method comment — best-effort, nothing actionable to surface.
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

        // Failed row → manual-recovery affordances: a link to the asset and a
        // local import that satisfies the item without the auto-download.
        if (!r.Success)
            stack.Children.Add(BuildRecoveryRow(r));

        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        return grid;
    }

    // The recovery row for one failed item. The item id resolves the asset URL
    // and the import kind through the catalogs: a model entry means a single
    // file, the native runtime a folder of DLLs. The plan's other items
    // (VAD model, CamemBERT set, anytype-cli) get the manual-download link
    // only — their import path is a wizard re-run (Retry), not a local pick.
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

        // Local import exists only where a picked file/folder can satisfy the
        // item in place: a catalog model (single checksum-verified file) or
        // the native runtime (folder of DLLs).
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

    // Picks a local copy of the failed asset and installs it in place: a folder
    // of DLLs for the native runtime, a single file (checksum-verified against
    // the catalog) for a model. On success the row is marked done and the whole
    // page re-derives its state; on a wrong file the user is told, and the row
    // stays failed.
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
                    status.Text = Loc.Get("Setup_ImportChecksumMismatch");
                    button.IsEnabled = true;
                    return;
                }

                MarkImported(r.ItemId, r.DisplayName, new FileInfo(dest).Length);
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
                    status.Text = Loc.Format("Setup_ImportFailed_Format",
                        string.Join(", ", NativeRuntime.GetMissing()));
                    button.IsEnabled = true;
                    return;
                }

                MarkImported(r.ItemId, r.DisplayName, null);
            }

            // Row satisfied — re-derive the whole page (footer flips to "Get
            // started" once nothing is left failing).
            ApplyState();
        }
        catch (Exception ex)
        {
            status.Text = Loc.Format("Setup_ImportFailed_Format", ex.Message);
            button.IsEnabled = true;
        }
    }

    // Replaces the matching result with a successful one, in place, so the
    // AllSucceeded gate and the rendered rows both reflect the import.
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
        catch { /* best-effort cleanup */ }
    }

    private static int CountFailed(SetupContext ctx)
    {
        int n = 0;
        foreach (var r in ctx.Results) if (!r.Success) n++;
        return n;
    }

}
