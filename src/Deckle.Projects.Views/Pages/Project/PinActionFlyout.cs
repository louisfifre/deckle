using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Deckle.Projects.Views;

// "Pin an action..." (spec, Pinned actions): the entries the project's script
// menu gives, where the person pins and unpins them and marks one pinned entry
// as the main action. The spec draws no such list; this is a flyout under the
// button, a native Flyout holding one row per entry.
//
// An entry with variants has one row per variant, because an entry is pinned
// with one of them ("self-check.ps1 · Debug"). A pin whose entry the menu no
// longer gives still has a row, at the end, so it can be unpinned: its card
// would otherwise stay on the page for good.
internal static class PinActionFlyout
{
    // read gives the listing when the page does not hold one yet; the flyout
    // opens at once with a progress ring and fills when it arrives.
    public static Flyout Build(
        ProjectsContext context, string projectId, ScriptMenuListing? known, Func<Task<ScriptMenuListing>> read) =>
        new()
        {
            Content = new PinActionList(context, projectId, known, read),
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
        };
}

internal sealed class PinActionList : UserControl
{
    private sealed record Row(ActionRef Action, string Label, string Script, ToggleButton Pin, ToggleButton Main);

    private const double ListWidth = 420;

    private readonly ProjectsContext _context;
    private readonly string _projectId;
    private readonly List<Row> _rows = [];
    private bool _gone;

    public PinActionList(ProjectsContext context, string projectId, ScriptMenuListing? known, Func<Task<ScriptMenuListing>> read)
    {
        _context = context;
        _projectId = projectId;
        Width = ListWidth;
        // A flyout closed before the menu answered must not be filled after.
        Unloaded += (_, _) => _gone = true;

        if (known is ScriptMenuListing.Listed listed) Show(listed.Entries);
        else
        {
            ShowReading();
            _ = ReadAsync(read);
        }
    }

    // The rows' pin toggles, for the self-check.
    public int RowCount => _rows.Count;

    private async Task ReadAsync(Func<Task<ScriptMenuListing>> read)
    {
        ScriptMenuListing listing;
        try
        {
            listing = await read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DeckleProjectsSource.Log.ProjectsPageFailed("project", ex.GetType().Name);
            listing = new ScriptMenuListing.Unreadable("");
        }

        if (_gone) return;
        if (listing is ScriptMenuListing.Listed listed) Show(listed.Entries);
        else ShowMessage(ProjectsText.Get("Pinned_MenuError"));
    }

    private void ShowReading()
    {
        var ring = new ProgressRing { Width = 20, Height = 20, IsActive = true, VerticalAlignment = VerticalAlignment.Center };
        TextBlock text = Themed.Text("TextFillColorSecondaryBrush");
        text.Text = ProjectsText.Get("Pinned_FlyoutReading");
        text.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(ring, text.Text);
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { ring, text } };
    }

    private void ShowMessage(string message)
    {
        TextBlock text = Themed.Text("TextFillColorSecondaryBrush");
        text.Text = message;
        text.TextWrapping = TextWrapping.Wrap;
        Content = text;
    }

    private void Show(IReadOnlyList<ScriptMenuEntry> entries)
    {
        _rows.Clear();
        var list = new StackPanel { Spacing = 4 };

        foreach (ScriptMenuEntry entry in entries)
        {
            IReadOnlyList<string?> variants = entry.Variants.Count == 0 ? [null] : [.. entry.Variants];
            foreach (string? variant in variants)
                list.Children.Add(MakeRow(new ActionRef(entry.Id, variant), entry.Label, entry.Script, variant, gone: false));
        }

        if (_context.Store.Find(_projectId) is { } project)
        {
            foreach (PinnedAction pin in project.Pinned)
            {
                if (entries.Any(e => e.GivesPin(pin))) continue;
                list.Children.Add(MakeRow(pin.Action, pin.Label, pin.Script, pin.Action.Variant, gone: true));
            }
        }

        if (_rows.Count == 0)
        {
            ShowMessage(ProjectsText.Get("Pinned_FlyoutEmpty"));
            return;
        }

        TextBlock hint = Themed.Text("TextFillColorSecondaryBrush", "CaptionTextBlockStyle");
        hint.Text = ProjectsText.Get("Pinned_FlyoutMainHint");
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 8, 0, 0);

        Content = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new ScrollViewer
                {
                    MaxHeight = 360,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = list,
                },
                hint,
            },
        };
        Sync();
    }

    private UIElement MakeRow(ActionRef action, string label, string script, string? variant, bool gone)
    {
        string detail = gone
            ? ProjectsText.Get("Pinned_FlyoutGone")
            : variant is null ? script : $"{script} · {variant}";

        TextBlock name = new() { Text = label, TextTrimming = TextTrimming.CharacterEllipsis };
        name.Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"];
        TextBlock sub = Themed.Text("TextFillColorTertiaryBrush", "CaptionTextBlockStyle");
        sub.Text = detail;
        sub.TextTrimming = TextTrimming.CharacterEllipsis;

        var pin = new ToggleButton { Content = ProjectsText.Get("Pinned_FlyoutPin"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(pin, ProjectsText.Format("Pinned_FlyoutPin_Format", label));
        var main = new ToggleButton { Content = ProjectsText.Get("Pinned_FlyoutMain"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(main, ProjectsText.Format("Pinned_FlyoutMain_Format", label));

        var row = new Row(action, label, script, pin, main);
        _rows.Add(row);
        pin.Click += (_, _) => OnPinClick(row);
        main.Click += (_, _) => OnMainClick(row);

        var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, sub } };
        Grid.SetColumn(pin, 1);
        Grid.SetColumn(main, 2);
        grid.Children.Add(text);
        grid.Children.Add(pin);
        grid.Children.Add(main);
        return grid;
    }

    private void OnPinClick(Row row)
    {
        if (row.Pin.IsChecked == true)
            _context.Store.Pin(_projectId, new PinnedAction(row.Action, row.Label, row.Script));
        else
            _context.Store.Unpin(_projectId, row.Action);
        Sync();
    }

    private void OnMainClick(Row row)
    {
        // A single choice among the pinned rows; pressing the main one again
        // leaves the project with no main action.
        _context.Store.SetMain(_projectId, row.Main.IsChecked == true ? row.Action : null);
        Sync();
    }

    // Draws the toggles as the store holds them. The rows themselves stay, so
    // the button that was pressed keeps the focus.
    private void Sync()
    {
        ProjectEntry? project = _context.Store.Find(_projectId);
        foreach (Row row in _rows)
        {
            bool pinned = project?.IsPinned(row.Action) == true;
            row.Pin.IsChecked = pinned;
            // "Main" keeps its place when it is not offered: collapsed, it
            // would give its column back and "Pin" would jump left under the
            // pointer, so a second click would land on "Main".
            row.Main.Opacity = pinned ? 1 : 0;
            row.Main.IsHitTestVisible = pinned;
            row.Main.IsTabStop = pinned;
            AutomationProperties.SetAccessibilityView(row.Main, pinned
                ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content
                : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            row.Main.IsChecked = pinned && project?.Main == row.Action;
        }
    }
}
