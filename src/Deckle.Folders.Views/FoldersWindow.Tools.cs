using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Deckle.Folders.Views;

// The tools: their buttons in the top row, the active one's view in the
// centre, and the window's side of the tool seam (IFoldersToolHost).
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

    private IFoldersTool ActiveTool => _activeTool ??= _tools[0];

    private void BuildToolButtons()
    {
        foreach (IFoldersTool tool in _tools)
        {
            var name = new TextBlock { Text = tool.Name, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new FontIcon { Glyph = tool.Glyph, FontSize = 16 });
            content.Children.Add(name);
            var button = new ToggleButton
            {
                Content = content,
                Height = 32,
                MinWidth = 36,
                Padding = new Thickness(10, 0, 10, 0),
                Tag = name,
            };
            ToolTipService.SetToolTip(button, tool.Name);
            AutomationProperties.SetName(button, tool.Name);
            button.Click += (_, _) => SwitchTo(tool);
            _toolButtons[tool.Id] = button;
            ToolButtons.Children.Add(button);
        }
        MarkActiveTool();
    }

    // The active tool's button is pressed and shows its name; the others
    // show their glyph alone (spec, Window).
    // With no folder added there is nothing to look at, so no tool is
    // active either (12.11 draws every tool as a bare, disabled glyph).
    private void MarkActiveTool()
    {
        foreach (IFoldersTool tool in _tools)
        {
            ToggleButton button = _toolButtons[tool.Id];
            bool active = ReferenceEquals(tool, ActiveTool) && _folders.Folders.Count > 0;
            button.IsChecked = active;
            ((TextBlock)button.Tag).Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }
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
        DetailsHost.Content = details;
        DetailsPanel.Visibility = details is null ? Visibility.Collapsed : Visibility.Visible;
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
