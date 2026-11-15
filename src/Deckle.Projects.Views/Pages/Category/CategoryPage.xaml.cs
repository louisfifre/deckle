using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// A category's page (board C1). Skeleton: it shows the title. The page step
// fills it in (breadcrumb, rows, reorder, delete) and keeps this constructor,
// which is the one the window calls.
internal sealed partial class CategoryPage : Page, IProjectsPage
{
    private readonly ProjectsContext _context;
    private readonly string _categoryId;

    public CategoryPage(ProjectsContext context, string categoryId)
    {
        _context = context;
        _categoryId = categoryId;
        InitializeComponent();
        TitleText.Text = _context.Store.FindCategory(_categoryId)?.Name ?? "";
    }

    public Task RefreshAsync(RefreshReason reason) => Task.CompletedTask;

    public void Dispose()
    {
    }
}
