using System.Diagnostics;
using Deckle.Shell.Taskbar;
using Deckle.Shell.Taskbar.Harness;

// Development console for the taskbar module's native engine.
//
//   Deckle.Shell.Taskbar.Harness [--dll <path>] [--root <dir>] [--send <json file>]
//                                [--tree <out file>] [--tree-after <seconds>]
//                                [--stop-after <seconds>] [--control <file>] [--visual]
//                                [--target explorer|startmenu]
//                                [--dump <out file>] [--dump-after <seconds>]
//                                [--doc-out <file> [--bg native|transparent|black] [--tray-pitch <px>]
//                                    [--tighten-quick-settings] [--clock-date-above-time]
//                                    [--tighten-clock-button] [--task-button-pitch <px>]
//                                    [--start-menu-full-height] [--start-menu-width fill|<px>]]
//
//   Deckle.Shell.Taskbar.Harness customizer [--control <file>] [--stop-after <seconds>]
//                                (see CustomizerCommand: the app's own wiring, no window)
//
// It injects the DLL into the running explorer, prints every record the DLL
// logs, injects again each time explorer rebuilds its taskbar, and on Ctrl+C
// (or --stop-after, or a "stop" line in the --control file) asks the DLL to
// restore and unload, then checks that explorer let the file go.
//
// --visual     also print the visual_add / visual_remove records (hundreds of lines).
// --target     explorer (default) or startmenu (StartMenuExperienceHost, found again
//              and injected again whenever Windows relaunches it).
// --send       sends a JSON document (the "apply" command) each time the DLL connects.
// --tree       writes the rebuilt visual trees to a file, --tree-after seconds
//              after the DLL connected (default 6), and once more on stop.
// --dump       asks the engine for the live visual trees (actual sizes, after the
//              rules applied) and writes them to a file, --dump-after seconds after
//              the DLL connected (default 6).
// --doc-out    writes the settings document of the --target, built from the --bg,
//              --tray-pitch, --tighten-quick-settings, --clock-date-above-time and
//              --start-menu-full-height options, to a file,
//              and exits without touching explorer. Send it later with --send or "send <file>".
// --control    polls a file; each line is a command (stop, ping, send <file>,
//              tree <file>, dump <file>), and the file is deleted once read.
//              "tree" writes what was recorded when elements were added, "dump" asks
//              the engine for the trees as they are now.
try
{
    return args.Length > 0 && args[0] == "customizer"
        ? await CustomizerCommand.RunAsync(args[1..])
        : await HarnessCommand.RunAsync(args);
}
catch (Exception exception)
{
    Console.Error.WriteLine("harness: " + exception);
    return 1;
}

