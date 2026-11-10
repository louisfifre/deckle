using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Deckle.Atelier.Views;

// The tools: their buttons in the top row, the active one's view in the
// centre, and the window's side of the tool seam (IAtelierToolHost).
public sealed partial class AtelierWindow : IAtelierToolHost
{
    private readonly IReadOnlyList<IAtelierTool> _tools = AtelierToolCatalog.Create();
    private readonly Dictionary<string, IAtelierToolView> _views = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToggleButton> _toolButtons = new(StringComparer.Ordinal);
    private readonly IFileIconProvider _icons = new NeutralFileIconProvider();
    private IAtelierTool? _activeTool;
    private CancellationTokenSource? _showing;
    private string? _selection;

    IFileIconProvider IAtelierToolHost.Icons => _icons;

    private IAtelierTool ActiveTool => _activeTool ??= _tools[0];

    private void BuildToolButtons()
    {
        foreach (IAtelierTool tool in _tools)
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
    private void MarkActiveTool()
    {
        foreach (IAtelierTool tool in _tools)
        {
            ToggleButton button = _toolButtons[tool.Id];
            bool active = ReferenceEquals(tool, ActiveTool);
            button.IsChecked = active;
            ((TextBlock)button.Tag).Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Changing tool keeps the folder and the selection (spec, Window).
    private void SwitchTo(IAtelierTool tool)
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

        IAtelierTool tool = ActiveTool;
        var clock = Stopwatch.StartNew();
        try
        {
            IAtelierToolView view = ViewOf(tool);
            CentreHost.Content = view.Content;
            if (_selection is null) Select(null, null);
            await view.ShowAsync(place, _selection, showing.Token);
            if (!showing.IsCancellationRequested)
                DeckleAtelierSource.Log.ToolShown(tool.Id, clock.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer place or tool.
        }
        catch (Exception ex)
        {
            DeckleAtelierSource.Log.ToolFailed(tool.Id, ex.GetType().Name);
            if (!showing.IsCancellationRequested) LastToolFailure = ex;
        }
    }

    private IAtelierToolView ViewOf(IAtelierTool tool)
    {
        if (!_views.TryGetValue(tool.Id, out IAtelierToolView? view))
        {
            view = tool.CreateView(this);
            _views[tool.Id] = view;
        }
        return view;
    }

    void IAtelierToolHost.Open(FolderPlace place) => _ = NavigateTo(place);

    void IAtelierToolHost.Select(string? path, UIElement? details) => Select(path, details);

    private void Select(string? path, UIElement? details)
    {
        _selection = path;
        DetailsHost.Content = details;
        DetailsPanel.Visibility = details is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
