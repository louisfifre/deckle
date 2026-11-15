using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// What the self-check drives to photograph a category's page (board C1).
public sealed partial class ProjectsWindow
{
    // Shows a category's page by its title, as a click on its header does;
    // false when the store holds no such category.
    public bool ShowCategoryForReview(string name)
    {
        ProjectCategory? category = _workspace.Store.Categories.FirstOrDefault(c => c.Name == name);
        if (category is null) return false;
        ShowForReview(new ProjectsTarget.Category(category.Id));
        return true;
    }

    // The "Delete this category" button of the page shown, or null on another page.
    public Button? DeleteCategoryButtonForReview => (_page as CategoryPage)?.DeleteLink;
}
