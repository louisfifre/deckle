using Microsoft.UI.Xaml;

namespace Deckle.Projects.Views;

// What Home's buttons and its cards' menus do. Each one changes the store (or
// the place) and nothing else: the page draws itself again from the store's
// change, so there is one way a card gets where it goes.
internal sealed partial class HomePage : IProjectCardCommands
{
    private void OnNewCategoryClick(object sender, RoutedEventArgs e) => NewCategory();

    private void OnAddProjectClick(object sender, RoutedEventArgs e) => _ = AddProjectAsync();

    // "New category": a category is added at the end, with its title already a
    // text box to type in. Enter keeps what was typed; Esc keeps the default
    // title, since making a category and silently deleting it would be a
    // surprise the spec does not ask for.
    private ProjectCategory NewCategory()
    {
        ProjectCategory category = _context.Store.CreateCategory();
        _renameCategoryId = category.Id;
        return category;
    }

    // The picker and the rules for a folder that is already a project are the
    // context's: the category page asks for the same thing.
    private Task AddProjectAsync() => _context.AddProjectAsync();

    public void Move(string projectId, string? categoryId) => _context.Store.MoveProject(projectId, categoryId);

    // "Move to a new category...": the same flow as "New category", with the
    // project already inside it.
    public void MoveToNewCategory(string projectId)
    {
        ProjectCategory category = NewCategory();
        _context.Store.MoveProject(projectId, category.Id);
    }

    // "Rename" opens the project's page with its name already a text box.
    public void Rename(string projectId) => _context.Navigate(new ProjectsTarget.Project(projectId, BeginRename: true));

    // No confirmation: nothing on the disk changes and adding the folder again
    // is one gesture. The menu disables the entry while an action of the
    // project runs; the workspace refuses all the same.
    public void Remove(string projectId) => _context.Workspace.RemoveProject(projectId);
}
