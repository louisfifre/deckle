using Deckle.Shell.WindowChrome;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// The side pane (board H2): "All projects", then each category as a node holding
// its projects, then "Without a category" as a node. "All projects" shows Home,
// a category shows its page, a project shows its page (spec, Window).
//
// Its shape follows specs/navigation-pane/deckle-navigation-pane.md through NavigationPane: the
// rail or open beside the page as the user chose, over the page in a narrow
// window. The window opens with the rail the first time (spec, Window); only the
// hamburger opens it.
public sealed partial class ProjectsWindow
{
    private NavigationPane _pane = null!;
    private bool _rebuildingPane;

    // Nodes the person opened or closed, by key. A node absent from it is in its
    // default state: a category open, "Without a category" closed (board H2).
    private readonly Dictionary<string, bool> _nodeOpen = [];

    private const string WithoutCategoryKey = "without-a-category";

    private void AttachPane() => _pane = NavigationPane.Attach(Nav, windowKey: "projects", openAtFirst: false);

    private void OnPaneToggleRequested(TitleBar sender, object args) => _pane.Toggle();

    // Draws the pane from the store. Items carry their place in Tag and the pane
    // hears one ItemInvoked for all of them: a handler on each item made the
    // Folders's pane fail to open once its items were rebuilt (the cause inside
    // NavigationView was not established there), so none is attached here.
    private void RebuildPane()
    {
        _rebuildingPane = true;
        try
        {
            ProjectStore store = _workspace.Store;
            Nav.MenuItems.Clear();

            Nav.MenuItems.Add(new NavigationViewItem
            {
                Content = ProjectsText.Get("Pane_AllProjects"),
                Icon = new FontIcon { Glyph = ProjectsGlyphs.Home },
                Tag = new ProjectsTarget.Home(),
            });
            Nav.MenuItems.Add(new NavigationViewItemSeparator());

            foreach (ProjectCategory category in store.Categories)
            {
                var node = new NavigationViewItem
                {
                    Content = category.Name,
                    Icon = new FontIcon { Glyph = ProjectsGlyphs.Folder },
                    Tag = new ProjectsTarget.Category(category.Id),
                };
                AddProjects(node, store.ProjectsIn(category.Id));
                node.IsExpanded = _nodeOpen.GetValueOrDefault(category.Id, true);
                Nav.MenuItems.Add(node);
            }

            IReadOnlyList<ProjectEntry> loose = store.ProjectsIn(null);
            if (loose.Count > 0)
            {
                // A node that only opens and closes: the spec gives it no page.
                var node = new NavigationViewItem
                {
                    Content = ProjectsText.Get("Pane_WithoutCategory"),
                    Icon = new FontIcon { Glyph = ProjectsGlyphs.Folder },
                    Tag = WithoutCategoryKey,
                    SelectsOnInvoked = false,
                };
                AddProjects(node, loose);
                node.IsExpanded = _nodeOpen.GetValueOrDefault(WithoutCategoryKey, false);
                Nav.MenuItems.Add(node);
            }
        }
        finally
        {
            _rebuildingPane = false;
        }
        SelectPaneItem();
    }

    private static void AddProjects(NavigationViewItem node, IReadOnlyList<ProjectEntry> projects)
    {
        foreach (ProjectEntry project in projects)
        {
            var item = new NavigationViewItem
            {
                Content = project.DisplayName,
                Tag = new ProjectsTarget.Project(project.Id),
            };
            ToolTipService.SetToolTip(item, project.Path);
            node.MenuItems.Add(item);
        }
    }

    // The item that stands for the place shown is the selected one. A project
    // under a node that is closed marks the node, as NavigationView does.
    private void SelectPaneItem()
    {
        if (_history.Current is not { } place) return;
        Nav.SelectedItem = FindPaneItem(Nav.MenuItems, place);
    }

    private static NavigationViewItem? FindPaneItem(IList<object> items, ProjectsTarget place)
    {
        foreach (object entry in items)
        {
            if (entry is not NavigationViewItem item) continue;
            if (item.Tag is ProjectsTarget tag && tag.Equals(place)) return item;
            if (FindPaneItem(item.MenuItems, place) is { } child) return child;
        }
        return null;
    }

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is not ProjectsTarget target) return;
        NavigateTo(target);
        // A pane that covers the page gets out of the way once a place is chosen;
        // a docked one stays, as Windows Settings' does.
        _pane.CloseIfOverPage();
    }

    // Remembers which nodes the person opened or closed, so a redraw of the pane
    // (any change in the store) leaves them as they were.
    private void OnNavExpanding(NavigationView sender, NavigationViewItemExpandingEventArgs args) =>
        RememberNode(args.ExpandingItemContainer, true);

    private void OnNavCollapsed(NavigationView sender, NavigationViewItemCollapsedEventArgs args) =>
        RememberNode(args.CollapsedItemContainer, false);

    private void RememberNode(NavigationViewItemBase node, bool open)
    {
        if (_rebuildingPane) return;
        string? key = node.Tag switch
        {
            ProjectsTarget.Category category => category.CategoryId,
            string without => without,
            _ => null,
        };
        if (key is not null) _nodeOpen[key] = open;
    }
}
