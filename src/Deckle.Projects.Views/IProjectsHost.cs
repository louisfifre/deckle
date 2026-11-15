using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Deckle.Projects.Views;

// What a page may ask of the window that shows it, and nothing more: the pages
// never hold the window itself, so they stay buildable and testable apart from
// it, and the window stays free to change how it navigates.
internal interface IProjectsHost
{
    DispatcherQueue Dispatcher { get; }

    // The island the window's controls live in; flyouts and pickers need it.
    XamlRoot? XamlRoot { get; }

    // Shows a place. Going to the place already shown changes nothing.
    void NavigateTo(ProjectsTarget target);

    // The title bar's back button: the place the person came from.
    void GoBack();

    // The system's folder picker, one folder; null when the person cancels.
    Task<string?> PickFolderAsync();
}
