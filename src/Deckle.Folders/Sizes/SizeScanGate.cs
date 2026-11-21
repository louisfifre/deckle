namespace Deckle.Folders;

// Holds a scan back before the folders it names, until it is opened: the seam
// the self-check uses to photograph an analysis midway (12.23), some rows
// measured and others still "…". Nothing in the app sets one.
//
// A held folder is set aside, not waited on, so no worker is tied up and the
// rest of the tree is measured whatever the number of workers; Open puts the
// folders set aside back in the scan's queue. A scan stopped before the gate
// opens simply never reads them, as Cancel does for any folder not read yet.
public sealed class SizeScanGate(Func<string, bool> holds)
{
    private readonly Lock _lock = new();
    private List<Action> _held = [];
    private bool _open;

    // How many folders are set aside at this moment.
    public int Held
    {
        get { lock (_lock) return _held.Count; }
    }

    public void Open()
    {
        List<Action> resume;
        lock (_lock)
        {
            _open = true;
            resume = _held;
            _held = [];
        }
        foreach (Action action in resume) action();
    }

    // True when the folder is set aside: the scan must not read it now, and
    // resume puts it back once the gate opens.
    internal bool Defer(string folder, Action resume)
    {
        lock (_lock)
        {
            if (_open || !holds(folder)) return false;
            _held.Add(resume);
            return true;
        }
    }
}
