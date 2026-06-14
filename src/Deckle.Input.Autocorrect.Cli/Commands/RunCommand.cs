using System.IO;
using Deckle.Core;
using Deckle.Input.Autocorrect;
using Deckle.Input;

namespace Deckle.Input.Autocorrect.Cli;

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
        bool trace = args.Has("--trace");

        string root = RepoPaths.RepoRoot();
        string dataDir = args.ValueOr("--data", RepoPaths.DefaultDataDir(root));

        // French-first: no bilingual guard, no language detection — the spelling
        // language is French. English is not loaded, so the restorer's guard and
        // the learning's "known English" check both fall away (each gates on a
        // non-null english). Unblocks ça→ça and the borrowed-word class; the eval
        // keeps English for the A/B (--no-en).
        var data = DataSet.Load(dataDir, wantEnglish: false, wantContext: true);
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
        // candidate lists, rebuilt per lookup (see BuildPersonalVariants).
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

        var host = new KeyboardInputHost();

        // Forensic mode: print every keyboard transition the system delivers,
        // attributed by origin — physical, injected by Deckle (InjectionTag in
        // ExtraInformation), or synthetic from a third party (remapper, RDP,
        // on-screen keyboard). This is how a mangled repair is pinned on the
        // stage that mangled it: our burst echoes back here exactly as sent;
        // anything extra or missing is someone else's hand. Console writes on
        // the input thread — diagnostic runs only.
        if (trace)
        {
            uint deckleTag = unchecked((uint)SendInputInterop.InjectionTag.ToInt64());
            host.KeyReceived += e =>
            {
                string origin = !e.IsInjected ? "phys"
                    : e.ExtraInfo == deckleTag ? "inj:deckle"
                    : e.ExtraInfo == 0 ? "inj:other"
                    : $"inj:other(0x{e.ExtraInfo:X8})";
                Console.WriteLine(
                    $"trace:     vk=0x{e.VirtualKey:X2} scan=0x{e.ScanCode:X4} {(e.IsKeyDown ? "down" : "up  ")} {origin}");
            };
        }

        var engine = new AutocorrectEngine(
            host: host,
            decoder: new KeyDecoder(),
            tracker: new TypedWordTracker(),
            prober: new SurfaceProber(),
            policy: policy,
            injector: new TextInjector(),
            settings: () => AutocorrectSettingsService.Instance.Current,
            dictionary: dictionary,
            french: data.French,
            english: data.English);

        // The surface line mirrors watch: editable/password make a silently
        // gated surface (enrolled but not editable) diagnosable at a glance.
        engine.SurfaceChanged += (s, enrolled) =>
            Console.WriteLine($"surface:   process=\"{NameOrUnknown(s.ProcessName)}\"  "
                            + $"editable={s.IsTextEditable}  password={s.IsPassword}  enrolled={enrolled}");
        engine.CorrectionApplied += d =>
            Console.WriteLine($"corrected: {d.Original} -> {d.Replacement} [{d.Reason}]");
        engine.CorrectionReverted += (original, replacement) =>
            Console.WriteLine($"reverted:  {replacement} -> {original}");
        engine.InjectionFailed += (original, replacement, revert) =>
            Console.WriteLine($"inject-fail: {(revert ? "revert " : "")}{original} -> {replacement}"
                            + "  (burst did not land — elevated target? partial send?)");

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
    // personal counterpart of the accent index. Rebuilt on every lookup:
    // adoption shifts with the decay clock, not only with mutations, so any
    // cache key short of time itself serves stale variants — and AdoptedWords
    // is already a decayed O(n) snapshot per call, cheap at commit granularity.
    private static Func<string, IReadOnlyList<AccentVariant>> BuildPersonalVariants(
        PersonalDictionary dictionary)
    {
        return key =>
        {
            List<AccentVariant>? match = null;
            foreach (string word in dictionary.AdoptedWords)
            {
                if (AccentFolding.Fold(word) != key)
                    continue;
                // Personal forms have no corpus frequency; a high constant
                // lets the engine's "personal wins ties" merge prefer them.
                (match ??= new List<AccentVariant>()).Add(new AccentVariant(word, double.MaxValue));
            }
            return match ?? (IReadOnlyList<AccentVariant>)Array.Empty<AccentVariant>();
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
        public CorrectionDecision? Evaluate(string word, IReadOnlyList<string> leftContext)
        {
            if (AccentFolding.Fold(word) == "francais")
                return new CorrectionDecision(
                    word, CasePattern.Apply(word, "français"), CorrectionReason.ToyHotstring);
            return null;
        }
    }
}
