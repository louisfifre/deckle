using Microsoft.UI.Xaml;

namespace Deckle.Projects.Views;

// "Delete this category" (spec): the category goes at once, with no
// confirmation, and its projects go back to Without a category; no project is
// removed. The window leaves the page by itself when the store says the
// category is gone (it drops the place from Back and shows Home), so the page
// only changes the store.
internal sealed partial class CategoryPage
{
    // The link, for the self-check to find and press.
    public Microsoft.UI.Xaml.Controls.HyperlinkButton DeleteLink => DeleteButton;

    private void OnDeleteClick(object sender, RoutedEventArgs e) => _context.Store.DeleteCategory(_categoryId);
}
