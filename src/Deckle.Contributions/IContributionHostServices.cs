using Microsoft.UI.Xaml;

namespace Deckle.Contributions;

// What the host runner needs from the application that runs it: the window
// mechanics that live in the executable (its log, its placement file, its
// theme), so the runner holds the rules and none of the plumbing, and a test
// can stand in for the application. The executable implements it once.
public interface IContributionHostServices : IContributionContext
{
    // Builds a window, naming the constructor's exception in the log before
    // it goes on (the constructor is where XAML parsing fails).
    Window CreateOrLog(string name, Func<Window> create);

    // Restores the window to where the person left it and saves its place
    // when it closes.
    void Place(Window window, string name);

    // Gives a newly created window the current theme.
    void ApplyTheme(Window window);

    // Calls `closed` once, when the window has closed.
    void WhenClosed(Window window, Action closed);

    // The window was shown on a person's request.
    void WindowOpened(string name);
}
