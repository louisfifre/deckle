using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Folders.Views;

// The tools: their buttons in the top row (all but Files, which shows when
// none is pressed), the active one's view in the centre, and the window's
// side of the tool seam (IFoldersToolHost).
public sealed partial class FoldersWindow : IFoldersToolHost
{
    private readonly IReadOnlyList<IFoldersTool> _tools = FoldersToolCatalog.Create();
    private readonly Dictionary<string, IFoldersToolView> _views = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToggleButton> _toolButtons = new(StringComparer.Ordinal);
    private readonly ShellFileIconProvider _icons = new();
    private IFoldersTool? _activeTool;
    private CancellationTokenSource? _showing;
    private string? _selection;

    IFileIconProvider IFoldersToolHost.Icons => _icons;

    // Files is the tool that shows when no button is pressed, and the one a
    // folder opens in: the first of the catalog, with no button of its own.
    private IFoldersTool DefaultTool => _tools[0];

    private IFoldersTool ActiveTool => _activeTool ??= DefaultTool;

    // Whether the panel may show: the person's choice (the toggle after the
    // tools), kept for this window's life only.
    private bool _detailsOn = true;

    // Whether a tool is showing a folder, as opposed to the empty state or
    // the message for an unreachable folder, where there is nothing to detail.
    private bool _showingFolder;

    // What the panel holds when the active tool gave no details for the
    // selection. Built once; the panel swaps it in and out of its host.
    private TextBlock? _nothingSelected;

    private TextBlock NothingSelected => _nothingSelected ??= new TextBlock
    {
        Text = FoldersText.Get("Details_NothingSelected"),
        Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Padding = new Thickness(24),
    };

    private void BuildToolButtons()
    {
        foreach (IFoldersTool tool in _tools.Where(t => !ReferenceEquals(t, DefaultTool)))
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new FontIcon { Glyph = tool.Glyph, FontSize = 16 });
            content.Children.Add(new TextBlock { Text = tool.Name, VerticalAlignment = VerticalAlignment.Center });
            var button = new ToggleButton
            {
                Content = content,
                Height = 32,
                MinWidth = 36,
                Padding = new Thickness(10, 0, 10, 0),
            };
            ToolTipService.SetToolTip(button, tool.Name);
            AutomationProperties.SetName(button, tool.Name);
            // A click on the pressed one gives the window back to Files.
            button.Click += (_, _) => SwitchTo(ReferenceEquals(tool, ActiveTool) ? DefaultTool : tool);
            _toolButtons[tool.Id] = button;
            ToolButtons.Children.Add(button);
        }
        MarkActiveTool();
    }

    // The pressed button is the active tool; with none pressed Files shows.
    // With no folder added there is nothing to look at, so no tool is
    // active either (12.11 draws every tool as a bare, disabled glyph).
    private void MarkActiveTool()
    {
        foreach ((string id, ToggleButton button) in _toolButtons)
            button.IsChecked = ActiveTool.Id == id && _folders.Folders.Count > 0;
    }

    // Changing tool keeps the folder and the selection (spec, Window).
    private void SwitchTo(IFoldersTool tool)
    {
        _activeTool = tool;
        MarkActiveTool();
        if (Place is not null) _ = ShowCurrentToolAsync();
    }

    // Shows the place with the active tool. A newer call cancels the one in
    // flight, so a slow folder never paints over the one the person went to.
    private async Task ShowCurrentToolAsync()
    {
        if (Place is not { } place) return;
        _showing?.Cancel();
        var showing = new CancellationTokenSource();
        _showing = showing;

        IFoldersTool tool = ActiveTool;
        var clock = Stopwatch.StartNew();
        try
        {
            IFoldersToolView view = ViewOf(tool);
            CentreHost.Content = view.Content;
            // The selection stays across tools, but what the panel holds is
            // the active tool's to say: it is emptied here and the tool fills
            // it again for the selection it is handed. Refresh's label too.
            Select(_selection, null);
            SetRefreshLabel(null);
            await view.ShowAsync(place, _selection, showing.Token);
            if (!showing.IsCancellationRequested)
                DeckleFoldersSource.Log.ToolShown(tool.Id, clock.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer place or tool.
        }
        catch (Exception ex)
        {
            DeckleFoldersSource.Log.ToolFailed(tool.Id, ex.GetType().Name);
            if (!showing.IsCancellationRequested) LastToolFailure = ex;
        }
    }

    private IFoldersToolView ViewOf(IFoldersTool tool)
    {
        if (!_views.TryGetValue(tool.Id, out IFoldersToolView? view))
        {
            view = tool.CreateView(this);
            _views[tool.Id] = view;
        }
        return view;
    }

    void IFoldersToolHost.Open(FolderPlace place) => _ = NavigateTo(place);

    void IFoldersToolHost.Select(string? path, UIElement? details) => Select(path, details);

    private void Select(string? path, UIElement? details)
    {
        _selection = path;
        DetailsHost.Content = details ?? NothingSelected;
        UpdateDetailsPanel();
    }

    // The panel shows when the person wants it and a tool shows a folder; a
    // click on an item hands it details but never turns the toggle on.
    private void UpdateDetailsPanel() =>
        DetailsPanel.Visibility = _detailsOn && _showingFolder ? Visibility.Visible : Visibility.Collapsed;

    private void OnDetailsToggleClick(object sender, RoutedEventArgs e)
    {
        _detailsOn = DetailsToggle.IsChecked == true;
        UpdateDetailsPanel();
    }

    private void OnDetailsAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        DetailsToggle.IsChecked = DetailsToggle.IsChecked != true;
        OnDetailsToggleClick(DetailsToggle, new RoutedEventArgs());
        args.Handled = true;
    }

    // Refresh's tooltip is one ToolTip the window keeps, so its text can
    // change while it stays attached to the button. The accessible name is
    // the action; the second line, when there is one, is its help text.
    private readonly ToolTip _refreshTip = new();

    void IFoldersToolHost.SetRefreshLabel(string? label, string? detail) => SetRefreshLabel(label, detail);

    private void SetRefreshLabel(string? label, string? detail = null)
    {
        label ??= FoldersText.Get("Nav_Refresh");
        _refreshTip.Content = detail is null ? label : label + Environment.NewLine + detail;
        if (!ReferenceEquals(ToolTipService.GetToolTip(RefreshButton), _refreshTip)) ToolTipService.SetToolTip(RefreshButton, _refreshTip);
        AutomationProperties.SetName(RefreshButton, label);
        AutomationProperties.SetHelpText(RefreshButton, detail ?? "");
    }

    // What the self-check reads instead of photographing the tooltip: a
    // tooltip opened on a window nobody sees is moved onto a monitor by WinUI
    // (WindowShortcutBugNotes.md), which the self-check's guard refuses.
    public string RefreshLabelForReview => _refreshTip.Content as string ?? "";
}
