using System.IO;
using Deckle.Core;
using Deckle.Input.Autocorrect;
using Deckle.Input.Autocorrect.Engine;
using Deckle.Input.Autocorrect.Learning;
using Deckle.Input.Autocorrect.Lexicon;
using Deckle.Input.Autocorrect.Surfaces;
using Deckle.Input.Autocorrect.Injection;
using Deckle.Input.Autocorrect.Tracking;
using Deckle.Input.Keyboard;

namespace Deckle.Input.Autocorrect.Cli.Commands;

// The live prototype: the whole engine wired to the real keyboard, repairing
// words on enrolled surfaces. It composes settings, lexicons, the personal
// dictionary, and the correction policy, then runs the AutocorrectEngine until
// Ctrl+C. `--toy` swaps the real policy for a one-rule hotstring so the
// injection/revert plumbing can be exercised without depending on the lexical
// gate firing.
internal static class RunCommand
{
    public static int Run(CliArgs args)
    {
        bool toy = args.Has("--toy");

        string root = RepoPaths.RepoRoot();
        string dataDir = args.ValueOr("--data", RepoPaths.DefaultDataDir(root));

        var data = DataSet.Load(dataDir, wantEnglish: true, wantContext: true);
        if (data is null) return 1; // Load already pointed at build-data.

        // Settings: the activation gate. Report what is enabled and which apps
        // are enrolled so the operator knows where corrections can land.
        var settings = AutocorrectSettingsService.Instance;
        var current = settings.Current;
        Console.WriteLine($"Enabled : {current.Enabled}");
        Console.WriteLine($"Enrolled: {Describe(current.EnrolledProcesses)}");
        if (current.EnrolledProcesses.Count == 0)
            Console.WriteLine("          (none — add one with `enroll add <process>`)");
        Console.WriteLine($"Policy  : {(toy ? "toy hotstring" : "diacritics restorer")}");
        Console.WriteLine();

        // Personal dictionary: the only persisted text in the module.
        string dictPath = Path.Combine(
            AppPaths.GetModuleDirectory("autocorrect"), "personal-dictionary.json");
        var dictionary = new PersonalDictionary(dictPath);

        // personalVariants delegate: fold the adopted words into AccentVariant
        // candidate lists. Caching keyed on AdoptedWords.Count — cheap, and a
        // count change is the only event that alters the adopted set in a run.
        var personalVariants = BuildPersonalVariants(dictionary);

        ICorrectionPolicy policy = toy
            ? new ToyHotstringPolicy()
            : new DiacriticsRestorer(
                french: data.French,
                english: data.English,
                index: data.Index,
                options: new RestorerOptions(), // live defaults; valid-forms stays off
                context: data.Context,
                personal: dictionary,
                personalVariants: personalVariants);

        var engine = new AutocorrectEngine(
            host: new KeyboardInputHost(),
            decoder: new KeyDecoder(),
            tracker: new TypedWordTracker(),
            prober: new SurfaceProber(),
            policy: policy,
            injector: new TextInjector(),
            settings: () => AutocorrectSettingsService.Instance.Current,
            dictionary: dictionary,
            french: data.French,
            english: data.English);

        engine.SurfaceChanged += (s, enrolled) =>
            Console.WriteLine($"surface:   process=\"{NameOrUnknown(s.ProcessName)}\"  enrolled={enrolled}");
        engine.CorrectionApplied += d =>
            Console.WriteLine($"corrected: {d.Original} -> {d.Replacement} [{d.Reason}]");
        engine.CorrectionReverted += (original, replacement) =>
            Console.WriteLine($"reverted:  {replacement} -> {original}");

        Console.WriteLine("Engine running. Ctrl+C to stop.");
        Console.WriteLine();

        if (!engine.Start())
        {
            Console.Error.WriteLine("Engine failed to start (keyboard host).");
            dictionary.Dispose();
            return 1;
        }

        WaitForCtrlC();
        engine.Stop();        // also flushes the dictionary
        dictionary.Dispose();
        Console.WriteLine("Stopped.");
        return 0;
    }

    // Builds the personalVariants delegate over the dictionary's adopted words.
    // It maps a folded key to the adopted surface forms that fold to it — the
    // personal counterpart of the accent index. The folded map is rebuilt only
    // when AdoptedWords.Count changes (the doctrine's good-enough cache key).
    private static Func<string, IReadOnlyList<AccentVariant>> BuildPersonalVariants(
        PersonalDictionary dictionary)
    {
        Dictionary<string, List<AccentVariant>>? folded = null;
        int builtForCount = -1;

        return key =>
        {
            var adopted = dictionary.AdoptedWords;
            if (folded is null || adopted.Count != builtForCount)
            {
                folded = new Dictionary<string, List<AccentVariant>>(StringComparer.Ordinal);
                foreach (string word in adopted)
                {
                    string f = AccentFolding.Fold(word);
                    if (!folded.TryGetValue(f, out var list))
                        folded[f] = list = new List<AccentVariant>();
                    // Personal forms have no corpus frequency; a high constant
                    // lets the engine's "personal wins ties" merge prefer them.
                    list.Add(new AccentVariant(word, double.MaxValue));
                }
                builtForCount = adopted.Count;
            }

            return folded.TryGetValue(key, out var variants)
                ? variants
                : (IReadOnlyList<AccentVariant>)Array.Empty<AccentVariant>();
        };
    }

    private static void WaitForCtrlC()
    {
        using var stop = new ManualResetEventSlim(false);
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stop.Set(); };
        Console.CancelKeyPress += handler;
        try { stop.Wait(); }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static string Describe(IReadOnlyCollection<string> processes) =>
        processes.Count == 0 ? "(none)" : string.Join(", ", processes);

    private static string NameOrUnknown(string name) => name.Length == 0 ? "(unknown)" : name;

    // The étape-2 toy: maps a folded "francais" to "français", honouring the
    // typed case shape. It leaves the lexicons untouched — its only job is to
    // make a correction fire deterministically so the injection/revert path can
    // be watched end to end.
    private sealed class ToyHotstringPolicy : ICorrectionPolicy
    {
        public CorrectionDecision? Evaluate(string word, string? previousWord)
        {
            if (AccentFolding.Fold(word) == "francais")
                return new CorrectionDecision(
                    word, CasePattern.Apply(word, "français"), CorrectionReason.ToyHotstring);
            return null;
        }
    }
}
