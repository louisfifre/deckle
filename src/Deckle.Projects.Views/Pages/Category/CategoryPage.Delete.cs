using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// "Delete this category" (spec): the button opens a small flyout that says what
// will happen, and its "Delete" button does it. The category goes, its projects
// go back to Without a category, and no project is removed. A flyout and not a
// dialog: the choice is quick and not blocking, and Esc or a click away cancels
// it. The window leaves the page by itself when the store says the category is
// gone (it drops the place from Back and shows Home), so the page only changes
// the store.
internal sealed partial class CategoryPage
{
    // The button, for the self-check to find and press.
    public Button DeleteLink => DeleteButton;

    // The sentence names the category and counts the projects it gives back, as
    // they are when the flyout opens (the list may have changed since the page
    // was drawn).
    private void OnDeleteFlyoutOpening(object? sender, object e)
    {
        string name = _context.Store.FindCategory(_categoryId)?.Name ?? "";
        int count = ProjectsHere().Count;
        DeleteQuestion.Text = count switch
        {
            0 => ProjectsText.Format("Category_DeleteQuestionNone_Format", name),
            1 => ProjectsText.Format("Category_DeleteQuestionOne_Format", name),
            _ => ProjectsText.Format("Category_DeleteQuestion_Format", name, count),
        };
    }

    private void OnDeleteConfirmed(object sender, RoutedEventArgs e)
    {
        DeleteFlyout.Hide();
        _context.Store.DeleteCategory(_categoryId);
    }
}