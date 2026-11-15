using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Projects.Views;

// One category on Home: its header, then its cards (board H1). A category with
// no project keeps its header and says in one sentence to drag a project onto
// it (spec: not drawn in v4, the sentence is the builder's).
//
// The whole section is the drop target, not only the words of its header, so a
// card can be let go anywhere over the category it should join (spec: "a card
// dragged onto a category joins it").
//
// A section is made once for a category and kept while the page lives: Update
// brings it to the store's current title and projects, so what is drawn in it
// (a card with the keyboard on it, a title being typed) survives a change.
internal sealed class CategorySection : UserControl
{
    private readonly TextBlock _hint;
    private readonly Action _openCategory;
    private string _name;

    public CategorySection(
        ProjectsContext context, IProjectCardCommands commands, ProjectCategory category, IReadOnlyList<ProjectEntry> projects)
    {
        CategoryId = category.Id;
        _name = category.Name;

        _openCategory = () => context.Navigate(new ProjectsTarget.Category(category.Id));

        Header = new CategoryHeader(category, projects.Count, _openCategory);
        Header.Renamed += name => context.Store.RenameCategory(category.Id, name);

        Cards = new ProjectGrid(context, commands);

        _hint = Themed.Text("TextFillColorSecondaryBrush", "CaptionTextBlockStyle");
        _hint.Text = ProjectsText.Get("Home_CategoryEmpty");

        var panel = new StackPanel
        {
            Spacing = 12,
            // A background, even a clear one, is what the pointer hits while
            // dragging over the gaps between the cards.
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Children = { Header, _hint, Cards },
        };

        FrameworkElement dropTarget = CategoryDropTarget.Attach(panel, () => _name, projectId => context.Store.Find(projectId)?.CategoryId == category.Id, projectId =>
        {
            // Dropped on the category it is already in: nothing to do.
            if (context.Store.Find(projectId)?.CategoryId != category.Id) commands.Move(projectId, category.Id);
        });

        Content = dropTarget;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        Update(category, projects);
    }

    public string CategoryId { get; }

    public CategoryHeader Header { get; }

    public ProjectGrid Cards { get; }

    // The category as the store has it now. An empty category shows its hint in
    // place of the grid; the grid is hidden, not removed, so it is kept.
    public void Update(ProjectCategory category, IReadOnlyList<ProjectEntry> projects)
    {
        _name = category.Name;
        Header.Update(category.Name, projects.Count);
        Cards.SetProjects(projects, category.Name, _openCategory);
        _hint.Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Cards.Visibility = projects.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
