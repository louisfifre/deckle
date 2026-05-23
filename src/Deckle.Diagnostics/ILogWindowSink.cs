namespace Deckle.Diagnostics;

// Contract for the live LogWindow surface. The viewer lives in
// Deckle.Diagnostics.Logging (Ui/LogWindow.xaml.cs) once the surface
// has been ported; during the migration window the App fills in a
// bridge implementation that forwards to the legacy LogWindow so
// EventSource emissions show up next to TelemetryService emissions.
//
// The contract is intentionally a single Write(EventEntry) — no
// filtering or formatting; that's the sink's job. The
// LogWindowEventListener at the Diagnostics layer pushes every Deckle.*
// event here unconditionally, and the sink applies whatever filter the
// user has set via the LogWindow SelectorBar.
public interface ILogWindowSink
{
    void Write(EventEntry entry);
}
