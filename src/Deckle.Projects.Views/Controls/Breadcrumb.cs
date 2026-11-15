using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Projects.Views;

// One step of the trail: its words and where it goes. A step with no action is
// plain text.
internal sealed record BreadcrumbStep(string Label, Action? Invoke);

// The trail above a page's title: "Projects ›" on a category's page,
// "Projects › Development" on a project's page, "Projects" alone for a project
// that is in no category. A step with an action is a button that goes there.
//
// Why not the native BreadcrumbBar: it draws the last step as the current page,
// bold and not clickable, and draws no chevron after it. The boards want the
// category's name clickable on a project's page and a chevron after "Projects"
// on a category's page, where the title sits on the line below. Matching them
// with the bar would mean restyling its items by hand, so this is a row of
// subtle buttons and the glyph Windows uses for the same chevron.
// (Judged from the bar's documented behaviour, not tried in the window.)
internal sealed class Breadcrumb : UserControl
{
    public Breadcrumb(IReadOnlyList<BreadcrumbStep> steps, bool trailingChevron)
    {
        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

        for (int i = 0; i < steps.Count; i++)
        {
            BreadcrumbStep step = steps[i];
            if (step.Invoke is { } invoke)
            {
                var button = new Button
                {
                    Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                    Content = step.Label,
                    Padding = new Thickness(6, 2, 6, 2),
                    MinHeight = 0,
                    Foreground = secondary,
                };
                button.Click += (_, _) => invoke();
                row.Children.Add(button);
            }
            else
            {
                row.Children.Add(new TextBlock { Text = step.Label, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
            }

            if (i < steps.Count - 1 || trailingChevron) row.Children.Add(Chevron(secondary));
        }

        Content = row;
        AutomationProperties.SetName(this, string.Join(" › ", steps.Select(s => s.Label)));
    }

    private static FontIcon Chevron(Brush brush) => new()
    {
        Glyph = ProjectsGlyphs.ChevronRight,
        FontSize = 12,
        Foreground = brush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // "Projects ›" for a category's page: Projects goes back to Home.
    public static Breadcrumb ForCategoryPage(ProjectsContext context) => new(
        [new BreadcrumbStep(ProjectsText.Get("Category_Breadcrumb_Projects"), () => context.Navigate(new ProjectsTarget.Home()))],
        trailingChevron: true);

    // "Projects › Development" for a project in a category, "Projects" for one
    // in none. The category's name goes to its page.
    public static Breadcrumb ForProjectPage(ProjectsContext context, ProjectEntry project)
    {
        List<BreadcrumbStep> steps =
        [
            new BreadcrumbStep(ProjectsText.Get("Project_Breadcrumb_Projects"), () => context.Navigate(new ProjectsTarget.Home())),
        ];
        if (project.CategoryId is { } categoryId && context.Store.FindCategory(categoryId) is { } category)
            steps.Add(new BreadcrumbStep(category.Name, () => context.Navigate(new ProjectsTarget.Category(categoryId))));
        return new Breadcrumb(steps, trailingChevron: false);
    }
}
