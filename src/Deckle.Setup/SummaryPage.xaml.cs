using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Transcription.Parakeet;
using Deckle.Localization;

namespace Deckle.Setup;

// Manual recovery: a failed row carries a "Download manually" link, so a
// blocked or filtered network doesn't dead-end the install: the person fetches
// the files by hand, and Retry takes what is already on disk as installed.
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

    // Recomputes the whole page from Results.
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
        // No title: the step header above already says whether it worked, so the bar holds only the fact the header does not (where, or how many).
        ResultBar.Title    = "";
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

        if (!r.Success && BuildRecoveryLink(r) is { } recovery)
            stack.Children.Add(recovery);

        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        return grid;
    }

    // A failed item offers the address its files can be fetched from by hand;
    // the recovery inside the wizard is Retry.
    private static HyperlinkButton? BuildRecoveryLink(InstallResult r)
    {
        string? url = r.ItemId switch
        {
            InstallPlan.ParakeetModelItemId => ParakeetModelCatalog.BrowseUrl,
            InstallPlan.SileroItemId        => Deckle.Vad.SileroVadModel.Url,
            InstallPlan.AnytypeItemId       => Deckle.Anytype.BackendInstallation.CurrentBundle.Url,
            _ => null,
        };

        return string.IsNullOrWhiteSpace(url)
            ? null
            : new HyperlinkButton
            {
                Content = Loc.Get("Setup_DownloadManually"),
                NavigateUri = new Uri(url),
                Margin = new Thickness(0, 6, 0, 0),
            };
    }
    private static int CountFailed(SetupContext ctx)
    {
        int n = 0;
        foreach (var r in ctx.Results) if (!r.Success) n++;
        return n;
    }

}
