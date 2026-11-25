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
    private CancellationTokenSource? _showing;
    private string? _selection;

    IFileIconProvider IFoldersToolHost.Icons => _icons;

    // Files is the tool that shows when no button is pressed, and the one a
    // folder opens in: the first of the catalog, with no button of its own.
    private IFoldersTool DefaultTool => _tools[0];

    // Each added folder remembers the tool it was left in: leave Photos for
    // another folder and that folder opens in its own tool, not in Photos.
    // So the tool is derived from the added folder the window stands in, read
    // fresh from the store because Place.Root is the record as it was when
    // the place was built. Home, no place, a folder never given a tool or a
    // tool id this build does not know all give the default tool.
    private IFoldersTool ActiveTool
    {
        get
        {
            if (Place is not { } place || _folders.Find(place.Root.Path)?.Tool is not { } id) return DefaultTool;
            return _tools.FirstOrDefault(t => t.Id == id) ?? DefaultTool;
        }
    }

    public string ActiveToolIdForReview => ActiveTool.Id;

    // Whether the panel may show: the person's choice (the toggle after the
    // tools), kept for this window's life only. Two choices, one per layout,
    // as NavigationPane keeps one per mode: the wide window starts with the
    // panel on, the narrow one (FoldersWindow.Widths.cs) without it, and
    // widening again gives back what the person had chosen in the wide one.
    private bool _detailsOnWide = true;
    private bool _detailsOnNarrow;

    private bool DetailsChoice => _narrow ? _detailsOnNarrow : _detailsOnWide;

    // Whether a tool is showing a folder, as opposed to Home or the message
    // for an unreachable folder, where there is nothing to detail.
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
            var name = new TextBlock { Text = tool.Name, VerticalAlignment = VerticalAlignment.Center };
            _toolNames.Add(name);
            content.Children.Add(name);
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
    // At Home, and so with no folder added, there is nothing for a tool to
    // look at, so none is pressed (12.11 draws every tool as a bare, disabled
    // glyph); each folder's tool is remembered and presses again in it.
    private void MarkActiveTool()
    {
        bool inFolder = _history.Current is FolderDestination;
        foreach ((string id, ToggleButton button) in _toolButtons)
            button.IsChecked = ActiveTool.Id == id && inFolder;
    }

    // Changing tool keeps the folder and the selection (spec, Window). The
    // choice is the folder's: it is saved in the added folder at once.
    private void SwitchTo(IFoldersTool tool)
    {
        if (Place is not { } place) return;
        StoreTool(place, tool);
        MarkActiveTool();
        _ = ShowCurrentToolAsync();
    }

    // The default tool is stored as none, so a folder never moved off Files
    // keeps no tool in its file.
    private void StoreTool(FolderPlace place, IFoldersTool tool) =>
        _folders.ChangeTool(place.Root.Path, ReferenceEquals(tool, DefaultTool) ? null : tool.Id);

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

    // What the toggle shows and the panel does are derived from the choice and
    // from what the window holds: where no panel can show (Home, the empty
    // state, an unreachable folder) the toggle is disabled and unpressed, and
    // the choice is kept to press again once a folder shows. A click on an
    // item hands it details but never turns the toggle on.
    private void UpdateDetailsPanel()
    {
        bool shown = DetailsChoice && _showingFolder;
        DetailsToggle.IsEnabled = _showingFolder;
        DetailsToggle.IsChecked = shown;
        DetailsPanel.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDetailsToggleClick(object sender, RoutedEventArgs e)
    {
        bool on = DetailsToggle.IsChecked == true;
        if (_narrow) _detailsOnNarrow = on;
        else _detailsOnWide = on;
        UpdateDetailsPanel();
    }

    private void OnDetailsAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Disabled where no panel can show: the key does what the button does.
        if (DetailsToggle.IsEnabled)
        {
            DetailsToggle.IsChecked = DetailsToggle.IsChecked != true;
            OnDetailsToggleClick(DetailsToggle, new RoutedEventArgs());
        }
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
