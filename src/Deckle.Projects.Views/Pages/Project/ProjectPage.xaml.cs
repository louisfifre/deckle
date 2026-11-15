using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// A project's page (boards P1 to P4). Skeleton: it shows the name and the path.
// The page step fills it in (header, rename, readouts, links, pinned actions)
// and keeps this constructor, which is the one the window calls. beginRename is
// true when the person chose "Rename" in a card's menu: the name is already a
// text box when the page opens.
internal sealed partial class ProjectPage : Page, IProjectsPage
{
    private readonly ProjectsContext _context;
    private readonly string _projectId;
    private readonly bool _beginRename;

    public ProjectPage(ProjectsContext context, string projectId, bool beginRename)
    {
        _context = context;
        _projectId = projectId;
        _beginRename = beginRename;
        InitializeComponent();

        if (_context.Store.Find(_projectId) is { } project)
        {
            TitleText.Text = project.DisplayName;
            PathText.Text = project.Path;
        }
    }

    public Task RefreshAsync(RefreshReason reason) => Task.CompletedTask;

    public void Dispose()
    {
    }
}
