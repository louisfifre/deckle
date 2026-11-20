namespace Deckle.Shell.WindowChrome;

// ─── Navigation pane state ────────────────────────────────────────────────────
//
// What specs/deckle-navigation-pane.md asks of every window's navigation, kept
// apart from the control so it can be tested without a window. Two facts make
// the whole state: whether the window has room for the rail beside the page
// (Docked), and what the user chose for such a window (OpenByChoice). A narrow
// window adds a third, short-lived one: the navigation opened over the page
// (OpenOverPage), which never touches the choice.
public sealed class NavigationPaneState
{
    public NavigationPaneState(bool openByChoice, bool docked)
    {
        OpenByChoice = openByChoice;
        Docked = docked;
    }

    // True while the window is wide enough for the rail beside the page: the
    // navigation is the rail or open, never over the page.
    public bool Docked { get; private set; }

    // What the user chose for a window wide enough: open, or the rail.
    public bool OpenByChoice { get; private set; }

    // In a narrow window only: the navigation is open over the page.
    public bool OpenOverPage { get; private set; }

    // Whether the control's pane is open, whatever the window's width.
    public bool PaneOpen => Docked ? OpenByChoice : OpenOverPage;

    // The window crossed the width where the rail fits, or not. Answers true
    // when the layout changed. Leaving the narrow band drops an open-over-page
    // state, so the docked navigation comes back as the user had left it.
    public bool SetDocked(bool docked)
    {
        if (docked == Docked) return false;
        Docked = docked;
        OpenOverPage = false;
        return true;
    }

    // The navigation button: docked, it changes the choice; narrow, it opens
    // or closes the navigation over the page and leaves the choice alone.
    public void Toggle() => SetOpen(!PaneOpen);

    public void SetOpen(bool open)
    {
        if (Docked) OpenByChoice = open;
        else OpenOverPage = open;
    }

    // The control closed its pane by itself: a place chosen or a click on the
    // page in a narrow window. A docked pane never closes by itself, so the
    // choice is not touched.
    public void ClosedOverPage()
    {
        if (!Docked) OpenOverPage = false;
    }
}
