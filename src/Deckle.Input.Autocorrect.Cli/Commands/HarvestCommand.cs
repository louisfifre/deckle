using System.IO;
using Deckle.Core;
using Deckle.Input.Autocorrect.Cli;
using Deckle.Input.Autocorrect;
using Deckle.Input;

namespace Deckle.Input.Autocorrect.Cli;

// Observation live: the `watch` pipeline (host → decode → track → probe, no
// correction, no injection, password-gated at the source) with a persistent,
// encrypted sink. It harvests the two signal streams the typo/spelling phase
// needs and the ASR corpora cannot give — the backspace-retape correction
// pairs, and the committed words the French lexicon does not know (the coverage
// gap). The raw typed stream is never persisted; only those two filtered
// streams reach disk.
//
// Opt-in (runs only when invoked), DPAPI-encrypted at rest, inspectable and
// purgeable here. A maintainer iteration tool, not a shipped feature.
internal static class HarvestCommand
{
    public static int Run(CliArgs args)
    {
        string harvestPath = Path.Combine(
            AppPaths.GetModuleDirectory("autocorrect"), "harvest.dat");

        if (args.Positional.Count > 0)
        {
            return args.Positional[0].ToLowerInvariant() switch
            {
                "path"  => PrintPath(harvestPath),
                "list"  => List(harvestPath),
                "purge" => Purge(harvestPath),
                _       => Unknown(),
            };
        }

        return Capture(args, harvestPath);
    }

    // The live capture loop. Mirrors WatchCommand for the observation pipeline
    // and the password gate; the difference is the persistent, filtered sink.
    private static int Capture(CliArgs args, string harvestPath)
    {
        // The lexicon classifies committed words as known / unknown. French-only,
        // no English guard — the spelling language is French (mirrors `run`).
        string root = RepoPaths.RepoRoot();
        string dataDir = args.ValueOr("--data", RepoPaths.DefaultDataDir(root));
        var data = DataSet.Load(dataDir, wantEnglish: false, wantContext: false);
        if (data is null) return 1; // Load already pointed at build-data.

        var host = new KeyboardInputHost();
        var decoder = new KeyDecoder();
        var tracker = new TypedWordTracker();
        var prober = new SurfaceProber();
        using var store = new HarvestStore(harvestPath);

        FocusedSurface surface = FocusedSurface.Unknown;
        bool mutedAnnounced = false;
        int edits = 0, unknowns = 0;

        // Committed word absent from the lexicon → persist (the coverage gap).
        tracker.WordCommitted += commit =>
        {
            if (!HarvestFilter.IsUnknownWord(commit.Word, data.French))
                return;
            store.RecordUnknownWord(commit.Word);
            unknowns++;
            Console.WriteLine($"unknown: \"{commit.Word}\"");
        };

        // Backspace-retape pair → persist (the typo-channel material).
        tracker.WordEdited += edit =>
        {
            if (!HarvestFilter.IsCorrectionPair(edit.Original, edit.Replacement))
                return;
            store.RecordEdit(edit.Original, edit.Replacement);
            edits++;
            Console.WriteLine($"edit:    \"{edit.Original}\" -> \"{edit.Replacement}\"");
        };

        host.FocusChanged += () =>
        {
            surface = prober.Probe();
            tracker.NotifyFocusChanged();
            if (!surface.IsPassword)
                mutedAnnounced = false;
        };

        host.PointerInteraction += () => tracker.NotifyPointerInteraction();

        host.KeyReceived += e =>
        {
            if (e.IsInjected) return;            // ignore our own / any synthetic input
            if (surface.IsPassword)              // hard gate — before decoding, so nothing is harvested
            {
                if (!mutedAnnounced)
                {
                    Console.WriteLine("         (password surface — not observed)");
                    mutedAnnounced = true;
                }
                return;
            }

            var stroke = decoder.Decode(e);
            if (stroke is not null)
                tracker.OnKeystroke(stroke.Value);
        };

        Console.WriteLine($"Harvesting to {harvestPath}");
        Console.WriteLine("Edit pairs and unknown words only — encrypted at rest, password surfaces never observed.");
        Console.WriteLine("Ctrl+C to stop.");
        Console.WriteLine();

        if (!host.Start())
        {
            Console.Error.WriteLine("Keyboard host failed to start.");
            return 1;
        }

        surface = prober.Probe(); // seed before the first focus event

        WaitForCtrlC();
        host.Stop();
        store.Flush();
        Console.WriteLine();
        Console.WriteLine($"Stopped. Captured {edits} edit(s) and {unknowns} unknown word(s) this session.");
        return 0;
    }

    private static int PrintPath(string harvestPath)
    {
        Console.WriteLine(harvestPath);
        return 0;
    }

    private static int List(string harvestPath)
    {
        using var store = new HarvestStore(harvestPath);
        var (edits, words) = store.Snapshot();

        Console.WriteLine($"Edit pairs ({edits.Count}):");
        if (edits.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
        else
        {
            Console.WriteLine($"  {"count",6}  pair");
            foreach (var e in edits)
                Console.WriteLine($"  {e.Count,6}  {e.Original} -> {e.Replacement}");
        }

        Console.WriteLine();
        Console.WriteLine($"Unknown words ({words.Count}):");
        if (words.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
        else
        {
            Console.WriteLine($"  {"count",6}  word");
            foreach (var w in words)
                Console.WriteLine($"  {w.Count,6}  {w.Word}");
        }

        return 0;
    }

    // Destructive — confirm on stdin. Default is No: anything but an explicit
    // "y"/"yes" leaves the harvest untouched.
    private static int Purge(string harvestPath)
    {
        Console.Write("Purge the entire harvest (edit pairs and unknown words)? [y/N] ");
        string? answer = Console.ReadLine();
        if (answer is not null &&
            (answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ||
             answer.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)))
        {
            using var store = new HarvestStore(harvestPath);
            store.Purge();
            Console.WriteLine("Purged.");
        }
        else
        {
            Console.WriteLine("Cancelled.");
        }

        return 0;
    }

    private static int Unknown()
    {
        Usage();
        return 1;
    }

    // Blocks until Ctrl+C, then returns so the caller can Stop() cleanly. The
    // handler cancels the default terminate so the shutdown (and final flush)
    // path runs.
    private static void WaitForCtrlC()
    {
        using var stop = new ManualResetEventSlim(false);
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stop.Set(); };
        Console.CancelKeyPress += handler;
        try { stop.Wait(); }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static void Usage() => Console.Error.WriteLine(
        "Usage: harvest [--data <dir>] | harvest list | harvest purge | harvest path");
}
