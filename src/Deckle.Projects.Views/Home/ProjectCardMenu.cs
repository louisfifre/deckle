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
//   Open in File Explorer
//   Open in terminal
//   ─────────
//   Move to <category>          one per category the project is not in
//   Move to a new category…
//   Move to Without a category  only on a project that is in a category
//   ─────────
//   Rename
//   Remove from Deckle          hint "Folder stays", off while an action runs
//
// There is no "Open": clicking the card does that.
internal static class ProjectCardMenu
{
    public static MenuFlyout Build(ProjectsContext context, ProjectEntry project, IProjectCardCommands commands)
    {
        string id = project.Id;
        var menu = new MenuFlyout();

        menu.Items.Add(Item(ProjectsText.Get("Menu_OpenInExplorer"), ProjectsGlyphs.FolderOpen, () => context.Workspace.Opener.OpenInExplorer(project.Path)));
        menu.Items.Add(Item(ProjectsText.Get("Menu_OpenInTerminal"), ProjectsGlyphs.Keyboard, () => context.Workspace.Opener.OpenInTerminal(project.Path)));
        menu.Items.Add(new MenuFlyoutSeparator());

        // Items with no glyph take the icon's column in a menu that has icons
        // elsewhere, so the words line up (the boards' "reserve-icon").
        foreach (ProjectCategory category in context.Store.Categories)
        {
            if (category.Id == project.CategoryId) continue;
            string categoryId = category.Id;
            menu.Items.Add(Item(ProjectsText.Format("Menu_MoveTo_Format", category.Name), null, () => commands.Move(id, categoryId)));
        }
        menu.Items.Add(Item(ProjectsText.Get("Menu_MoveToNewCategory"), ProjectsGlyphs.Add, () => commands.MoveToNewCategory(id)));
        if (project.CategoryId is not null)
            menu.Items.Add(Item(ProjectsText.Get("Menu_MoveToWithoutCategory"), null, () => commands.Move(id, null)));
        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(Item(ProjectsText.Get("Menu_Rename"), ProjectsGlyphs.Edit, () => commands.Rename(id)));

        MenuFlyoutItem remove = Item(ProjectsText.Get("Menu_Remove"), ProjectsGlyphs.Remove, () => commands.Remove(id));
        // The native way to show a hint at the right of an item.
        remove.KeyboardAcceleratorTextOverride = ProjectsText.Get("Menu_RemoveHint");
        // An action of the project runs: removing it would leave its terminal
        // with no page and its result nowhere (spec, Behaviours).
        remove.IsEnabled = context.Workspace.Board.ActiveRun(id) is null;
        menu.Items.Add(remove);
        return menu;
    }

    private static MenuFlyoutItem Item(string text, string? glyph, Action invoke)
    {
        var item = new MenuFlyoutItem { Text = text };
        if (glyph is not null) item.Icon = new FontIcon { Glyph = glyph };
        item.Click += (_, _) => invoke();
        return item;
    }
}