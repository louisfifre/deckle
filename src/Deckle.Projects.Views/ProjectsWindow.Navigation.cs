using System.Diagnostics;
using Deckle.Atelier;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Projects.Views;

// Where the window stands, and Back.
//
// One ContentPresenter holds the page, swapped by NavigateTo. There is no
// Frame: the window keeps its own history (Deckle Folders' NavigationHistory,
// which holds any class with Equals) so that "Rename" can open the same project
// page in a different arrival (ProjectsTarget.Project carries BeginRename), and
// so a place whose project or category was removed can be dropped from Back.
//
// A page is built at every visit and disposed when the person leaves, so what
// it started (a reading, a timer, a subscription) ends with it.
public sealed partial class ProjectsWindow
{
    private readonly NavigationHistory<ProjectsTarget> _history = new();
    private IProjectsPage? _page;

    // Set when a page threw while it was built; the self-check fails the image
    // that should have shown it.
    public Exception? LastPageFailure { get; private set; }

    void IProjectsHost.NavigateTo(ProjectsTarget target) => NavigateTo(target);

    void IProjectsHost.GoBack() => GoBack();

    private void NavigateTo(ProjectsTarget target)
    {
        if (_history.Navigate(target)) ShowCurrent();
    }

    private void GoBack()
    {
        if (_history.GoBack() is not null) ShowCurrent();
    }

    private void ShowCurrent()
    {
        ProjectsTarget target = _history.Current!;
        string kind = KindOf(target);
        var clock = Stopwatch.StartNew();

        _page?.Dispose();
        _page = null;
        LastPageFailure = null;

        try
        {
            Page page = CreatePage(target);
            _page = (IProjectsPage)page;
            PageHost.Content = page;
            DeckleProjectsSource.Log.ProjectsPageShown(kind, clock.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // A page that cannot be built leaves the pane and the other pages
            // alone: the window says so in its place instead of closing.
            LastPageFailure = ex;
            DeckleProjectsSource.Log.ProjectsPageFailed(kind, ex.GetType().Name);
            PageHost.Content = FailureMessage();
        }

        UpdateChrome();
        SelectPaneItem();
    }

    private Page CreatePage(ProjectsTarget target) => target switch
    {
        ProjectsTarget.Category category => new CategoryPage(_context, category.CategoryId),
        ProjectsTarget.Project project => new ProjectPage(_context, project.ProjectId, project.BeginRename),
        _ => new HomePage(_context),
    };

    private static string KindOf(ProjectsTarget target) => target switch
    {
        ProjectsTarget.Category => "category",
        ProjectsTarget.Project => "project",
        _ => "home",
    };

    private static UIElement FailureMessage()
    {
        var message = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(32),
            Spacing = 8,
            MaxWidth = 584,
        };
        message.Children.Add(new TextBlock
        {
            Text = ProjectsText.Get("Window_PageFailed"),
            Style = (Style)Application.Current.Resources["BodyLargeTextBlockStyle"],
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        TextBlock hint = Themed.Text("TextFillColorSecondaryBrush");
        hint.Text = ProjectsText.Get("Window_PageFailedHint");
        hint.TextAlignment = TextAlignment.Center;
        hint.TextWrapping = TextWrapping.Wrap;
        message.Children.Add(hint);
        return message;
    }

    // The store changed: the pane is drawn again, Back and Forward lose every
    // place that names a project or a category that is gone, and the title bar
    // follows. Raised by the store on whatever thread made the change, so the
    // window's thread does the work, once for changes that arrive together.
    private int _storeChangeQueued;

    private void OnStoreChanged()
    {
        if (Interlocked.Exchange(ref _storeChangeQueued, 1) == 1) return;
        _context.Dispatch(() =>
        {
            Interlocked.Exchange(ref _storeChangeQueued, 0);
            RebuildPane();
            PruneHistory();
            UpdateChrome();
        });
    }

    private void PruneHistory()
    {
        ProjectStore store = _workspace.Store;
        bool currentGone = _history.Forget(target => target switch
        {
            ProjectsTarget.Project project => store.Find(project.ProjectId) is null,
            ProjectsTarget.Category category => store.FindCategory(category.CategoryId) is null,
            _ => false,
        });
        if (currentGone) NavigateTo(new ProjectsTarget.Home());
    }

    private void OnBackRequested(TitleBar sender, object args) => GoBack();
}
