using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// Home before any project is added (board H0).
internal sealed partial class HomeEmptyState : UserControl
{
    public HomeEmptyState()
    {
        InitializeComponent();
        TitleText.Text = ProjectsText.Get("Home_EmptyTitle");
        BodyText.Text = ProjectsText.Get("Home_EmptyBody");
        AddLabel.Text = ProjectsText.Get("Home_AddProject");
    }

    public event Action? AddRequested;

    public Button AddProjectButton => AddButton;

    private void OnAddClick(object sender, RoutedEventArgs e) => AddRequested?.Invoke();
}
