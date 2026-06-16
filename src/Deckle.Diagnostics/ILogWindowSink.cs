namespace Deckle.Diagnostics;

// Contract for the live LogWindow surface. The viewer currently lives
// in Deckle.App and implements this sink so EventSource emissions can
// be rendered without the Diagnostics layer referencing XAML.
//
// The contract is intentionally a single Write(EventEntry) — no
// filtering or formatting; that's the sink's job. The
// LogWindowSink at the Diagnostics layer pushes every Deckle.*
// event here unconditionally, and the sink applies whatever filter the
// user has set via the LogWindow SelectorBar.
public interface ILogWindowSink
{
    void Write(EventEntry entry);
}
