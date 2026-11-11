namespace Deckle.Mail.Hub;

// Where the back button of the title bar, Alt+Left and the mouse back button
// lead. Every page the person reaches is pushed, a click in the navigation
// included, as Windows Settings does; the back button is disabled, not
// hidden, when there is nothing to go back to (spec, The window).
//
// Kept apart from the Frame's own back stack: a page that no longer exists (a
// mailbox removed, a subject whose mailbox went with it) is skipped on the
// way back, which the Frame cannot know.
public sealed class MailHistory
{
    private readonly List<MailDestination> _stack = [];

    public MailDestination? Current => _stack.Count == 0 ? null : _stack[^1];

    public bool CanGoBack => _stack.Count > 1;

    public int Depth => _stack.Count;

    // Pushes the page; the page already shown is not pushed twice.
    public void Go(MailDestination destination)
    {
        if (Current == destination) return;
        _stack.Add(destination);
    }

    // The page to show, or null when there is nowhere to go back to. Pages
    // the predicate refuses are dropped on the way.
    public MailDestination? Back(Func<MailDestination, bool>? stillExists = null)
    {
        while (_stack.Count > 1)
        {
            _stack.RemoveAt(_stack.Count - 1);
            if (stillExists is null || stillExists(_stack[^1])) return _stack[^1];
        }
        return null;
    }

    // A fresh start on one page: a scenario swapped, the window reopened.
    public void Reset(MailDestination destination)
    {
        _stack.Clear();
        _stack.Add(destination);
    }
}
