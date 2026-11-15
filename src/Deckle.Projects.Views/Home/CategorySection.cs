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
internal sealed class CategorySection : UserControl
{
    public CategorySection(
        ProjectsContext context, IProjectCardCommands commands, ProjectCategory category, IReadOnlyList<ProjectEntry> projects)
    {
        CategoryId = category.Id;

        Header = new CategoryHeader(category, projects.Count, () => context.Navigate(new ProjectsTarget.Category(category.Id)));
        Header.Renamed += name => context.Store.RenameCategory(category.Id, name);

        Cards = new ProjectGrid(context, commands);
        Cards.SetProjects(projects, category.Name, () => context.Navigate(new ProjectsTarget.Category(category.Id)));

        var panel = new StackPanel
        {
            Spacing = 12,
            // A background, even a clear one, is what the pointer hits while
            // dragging over the gaps between the cards.
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Children = { Header },
        };

        if (projects.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = ProjectsText.Get("Home_CategoryEmpty"),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
        }
        else
        {
            panel.Children.Add(Cards);
        }

        CategoryDropTarget.Attach(panel, category.Name, projectId =>
        {
            // Dropped on the category it is already in: nothing to do.
            if (context.Store.Find(projectId)?.CategoryId != category.Id) commands.Move(projectId, category.Id);
        });

        Content = panel;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }

    public string CategoryId { get; }

    public CategoryHeader Header { get; }

    public ProjectGrid Cards { get; }
}
