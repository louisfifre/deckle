using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// What a card's menu asks of the page that holds the card. The links (Explorer,
// the terminal) the menu opens itself through the workspace; what changes the
// store or the place is the page's.
internal interface IProjectCardCommands
{
    void Move(string projectId, string? categoryId);

    // "Move to a new category...": a category is made, the project goes into
    // it and its title is a text box to type in (spec, Home).
    void MoveToNewCategory(string projectId);

    // Opens the project's page with its name already a text box.
    void Rename(string projectId);

    void Remove(string projectId);
}

// A card's "..." menu, the only menu of a card; a right click on the card
// opens the same one (spec, Home, "A card"). Built each time it opens, because
// the categories to move to, and whether the project may be removed, change.
//
//   Open in File Explorer       off when the folder was not found
//   Open in terminal            off when the folder was not found
//   ─────────
//   Move to                  >  one item per category the project is not in
//                               ─────────
//                               New category…
//                               Without a category   only on a project that is in a category
//   ─────────
//   Rename
//   Remove from Deckle          tooltip "Folder stays", off while an action runs
//
// The categories go in a submenu, as File Explorer's "Send to" holds its
// targets, so the menu keeps its size however many categories there are. There
// is no "Open": clicking the card does that.
internal static class ProjectCardMenu
{
    public static MenuFlyout Build(ProjectsContext context, ProjectEntry project, IProjectCardCommands commands)
    {
        string id = project.Id;
        var menu = new MenuFlyout();

        // The reading says once it has been made; the menu does not look at the
        // disk itself, on the window's thread.
        bool folderMissing = context.Workspace.Readings.Get(id)?.Folder == FolderState.Missing;

        MenuFlyoutItem explorer = Item(ProjectsText.Get("Menu_OpenInExplorer"), ProjectsGlyphs.FolderOpen, () => context.Workspace.Opener.OpenInExplorer(project.Path));
        MenuFlyoutItem terminal = Item(ProjectsText.Get("Menu_OpenInTerminal"), ProjectsGlyphs.Keyboard, () => context.Workspace.Opener.OpenInTerminal(project.Path));
        if (folderMissing)
        {
            foreach (MenuFlyoutItem link in new[] { explorer, terminal })
            {
                link.IsEnabled = false;
                Describe(link, ProjectsText.Get("Project_Missing"));
            }
        }
        menu.Items.Add(explorer);
        menu.Items.Add(terminal);
        menu.Items.Add(new MenuFlyoutSeparator());

        var moveTo = new MenuFlyoutSubItem { Text = ProjectsText.Get("Menu_MoveTo"), Icon = new FontIcon { Glyph = ProjectsGlyphs.MoveToFolder } };
        foreach (ProjectCategory category in context.Store.Categories)
        {
            if (category.Id == project.CategoryId) continue;
            string categoryId = category.Id;
            moveTo.Items.Add(Item(category.Name, null, () => commands.Move(id, categoryId)));
        }
        if (moveTo.Items.Count > 0) moveTo.Items.Add(new MenuFlyoutSeparator());
        moveTo.Items.Add(Item(ProjectsText.Get("Menu_MoveToNewCategory"), null, () => commands.MoveToNewCategory(id)));
        if (project.CategoryId is not null)
            moveTo.Items.Add(Item(ProjectsText.Get("Menu_MoveToWithoutCategory"), null, () => commands.Move(id, null)));
        menu.Items.Add(moveTo);
        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(Item(ProjectsText.Get("Menu_Rename"), ProjectsGlyphs.Edit, () => commands.Rename(id)));

        MenuFlyoutItem remove = Item(ProjectsText.Get("Menu_Remove"), ProjectsGlyphs.Remove, () => commands.Remove(id));
        // An action of the project runs: removing it would leave its terminal
        // with no page and its result nowhere (spec, Behaviours).
        bool busy = context.Workspace.Board.ActiveRun(id) is not null;
        remove.IsEnabled = !busy;
        // "Folder stays" is not a shortcut, so it is not drawn where a menu
        // draws one (a screen reader reads that slot as the accelerator key):
        // it is the item's tooltip and help text, which is also where the reason
        // for a disabled item goes.
        string hint = ProjectsText.Get("Menu_RemoveHint");
        Describe(remove, busy ? ProjectsText.Get("Menu_RemoveBusy") + " " + hint : hint);
        menu.Items.Add(remove);
        return menu;
    }

    private static void Describe(MenuFlyoutItem item, string text)
    {
        ToolTipService.SetToolTip(item, text);
        AutomationProperties.SetHelpText(item, text);
    }

    private static MenuFlyoutItem Item(string text, string? glyph, Action invoke)
    {
        var item = new MenuFlyoutItem { Text = text };
        if (glyph is not null) item.Icon = new FontIcon { Glyph = glyph };
        item.Click += (_, _) => invoke();
        return item;
    }
}
