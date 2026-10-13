namespace Deckle.Diagnostics;

// Contract for the live LogWindow surface. The viewer lives in Deckle.App
// and implements this sink so EventSource emissions can be rendered
// without the Diagnostics layer referencing XAML.
//
// LogWindowSink pushes every operational entry here; the viewer applies
// the user's view filter itself.
public interface ILogWindowSink
{
    void Write(EventEntry entry);
}
