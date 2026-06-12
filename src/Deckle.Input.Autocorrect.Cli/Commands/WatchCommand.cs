using Deckle.Input.Autocorrect.Surfaces;
using Deckle.Input.Autocorrect.Tracking;
using Deckle.Input.Keyboard;

namespace Deckle.Input.Autocorrect.Cli.Commands;

// Étape-1 observation deliverable: host → decode → track → probe, with NO
// correction and NO injection. It exists to watch the observation layer
// behave on real typing — surface transitions and the tracker's commit /
// edit / reset events — before any repair logic is in play.
//
// The password gate is enforced here exactly as doctrine demands: while the
// focused surface is a password control, keystrokes are not decoded, not
// buffered, not counted. We mute the whole pipeline at the source.
internal static class WatchCommand
{
    public static int Run(CliArgs args)
    {
        var host = new KeyboardInputHost();
        var decoder = new KeyDecoder();
        var tracker = new TypedWordTracker();
        var prober = new SurfaceProber();

        // Surface state lives on the input thread; the key handler reads it to
        // gate decoding. volatile is enough — single writer (focus), single
        // reader (key), both on the input thread.
        FocusedSurface surface = FocusedSurface.Unknown;
        bool mutedAnnounced = false; // print "(muted)" once per password entry

        tracker.WordCommitted += commit =>
            Console.WriteLine($"committed: \"{commit.Word}\"  boundary='{Display(commit.Boundary)}'  "
                            + $"prev={Quote(commit.PreviousWord)}");
        tracker.WordEdited += edit =>
            Console.WriteLine($"edited:    \"{edit.Original}\" -> \"{edit.Replacement}\"");
        tracker.TrackerReset += reason =>
            Console.WriteLine($"reset:     {reason}");

        host.FocusChanged += () =>
        {
            surface = prober.Probe();
            tracker.NotifyFocusChanged();
            Console.WriteLine($"surface:   process={Quote(NameOrUnknown(surface.ProcessName))}  "
                            + $"editable={surface.IsTextEditable}  password={surface.IsPassword}");
            if (!surface.IsPassword)
                mutedAnnounced = false;
        };

        host.PointerInteraction += () => tracker.NotifyPointerInteraction();

        host.KeyReceived += e =>
        {
            if (e.IsInjected) return;            // ignore our own / any synthetic input
            if (surface.IsPassword)              // hard gate — before decoding
            {
                if (!mutedAnnounced)
                {
                    Console.WriteLine("           (password surface — muted)");
                    mutedAnnounced = true;
                }
                return;
            }

            var stroke = decoder.Decode(e);
            if (stroke is not null)
                tracker.OnKeystroke(stroke.Value);
        };

        Console.WriteLine("Watching keyboard / focus. Ctrl+C to stop.");
        Console.WriteLine();

        if (!host.Start())
        {
            Console.Error.WriteLine("Keyboard host failed to start.");
            return 1;
        }

        // Seed the surface before the first focus event.
        surface = prober.Probe();
        Console.WriteLine($"surface:   process={Quote(NameOrUnknown(surface.ProcessName))}  "
                        + $"editable={surface.IsTextEditable}  password={surface.IsPassword}");

        WaitForCtrlC();
        host.Stop();
        Console.WriteLine("Stopped.");
        return 0;
    }

    // Blocks until Ctrl+C, then returns so the caller can Stop() cleanly. The
    // handler cancels the default terminate so the shutdown path runs.
    private static void WaitForCtrlC()
    {
        using var stop = new ManualResetEventSlim(false);
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stop.Set(); };
        Console.CancelKeyPress += handler;
        try { stop.Wait(); }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static string NameOrUnknown(string name) => name.Length == 0 ? "(unknown)" : name;
    private static string Quote(string? s) => s is null ? "(none)" : $"\"{s}\"";
    private static string Display(char boundary) => boundary == ' ' ? "space" : boundary.ToString();
}
