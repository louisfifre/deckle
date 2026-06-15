using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Deckle.Core;
using Deckle.Input.Autocorrect;
using Deckle.Input;
using Deckle.Notifications;

namespace Deckle.App;

// Autocorrect module composition — same posture as App.Trackpad: the App owns
// the engine and reconciles it with the persisted module settings. The engine
// is the live diacritics restorer (lexical gate + bigram left-context), wired
// to the real keyboard, repairing words on enrolled surfaces. Enabled by
// default (AutocorrectSettings); corrections land only on enrolled processes
// (Notepad out of the box) and never on a password surface.
//
// The CamemBERT reranker is an offline eval/iteration tool only — the live
// engine uses the small bigram pair model, so the only runtime data is the two
// gzip lexicons shipped beside the executable under Data/.
public partial class App
{
    private AutocorrectEngine? _autocorrectEngine;
    private PersonalDictionary? _autocorrectDictionary;
    private bool _autocorrectStarted;

    // Builds the autocorrect engine off the UI thread. The lexicon load (gzip
    // decode + dictionary/index build of the multi-MB FR frequency data) is the
    // heavy part and runs on the thread pool; the cheap composition + settings
    // reconciliation resume on the UI thread. Boot never blocks on this —
    // OnLaunched fires it and moves on, and nothing else reads the engine
    // synchronously, so the deferral is race-free at startup.
    private async Task InitializeAutocorrectAsync()
    {
        try
        {
            // The keyboard/mouse Raw Input host is the process-shared one,
            // created by InitializeInputHost ahead of this. Without it there
            // is no input source to drive the engine.
            if (_keyboardMouseHost is null) return;

            string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
            string frenchPath = Path.Combine(dataDir, "lexicon-fr.tsv.gz");
            string pairPath = Path.Combine(dataDir, "pair-bigrams-fr.tsv.gz");

            // The French lexicon is the gate; without it there is nothing to do,
            // so leave autocorrect unbuilt rather than start a no-op engine.
            if (!File.Exists(frenchPath))
                return;

            // The heavy step: gzip decode + build of the FR frequency lexicon,
            // its accent index, and the pair bigram model. Pure CPU/IO, no UI
            // affinity — run it on the thread pool so boot is not blocked.
            var (french, index, context) = await Task.Run(() =>
            {
                var fr = FrequencyLexicon.LoadTsvGz(frenchPath);
                var idx = AccentIndex.Build(fr);
                var ctx = File.Exists(pairPath)
                    ? BigramPairDisambiguator.LoadTsvGz(pairPath, null)
                    : null;
                return (fr, idx, ctx);
            }).ConfigureAwait(true);

            // The only persisted text in the module — under the user data root,
            // inspectable and removable through the CLI `dict` command.
            string dictPath = Path.Combine(
                AppPaths.GetModuleDirectory("autocorrect"), "personal-dictionary.json");
            _autocorrectDictionary = new PersonalDictionary(dictPath);

            // French-first: no English guard. The bigram model resolves the
            // ambiguous residue; the reranker stays an offline tool.
            var policy = new DiacriticsRestorer(
                french: french,
                english: null,
                index: index,
                options: new RestorerOptions(),
                context: context,
                personal: _autocorrectDictionary,
                personalVariants: BuildAutocorrectPersonalVariants(_autocorrectDictionary));

            _autocorrectEngine = new AutocorrectEngine(
                host: _keyboardMouseHost,
                decoder: new KeyDecoder(),
                tracker: new TypedWordTracker(),
                prober: new SurfaceProber(),
                policy: policy,
                injector: new TextInjector(),
                settings: () => AutocorrectSettingsService.Instance.Current,
                dictionary: _autocorrectDictionary,
                french: french,
                english: null);

            // Reactive enrollment: a would-be correction on an undecided app
            // raises this on the engine's input thread. Detach the prompt so we
            // never block that thread; the user's answer writes the decision back.
            _autocorrectEngine.EnrollmentSuggested += p => _ = PromptAutocorrectEnrollmentAsync(p);

            AutocorrectSettingsService.Instance.Changed += ReconcileAutocorrect;
            ReconcileAutocorrect();
        }
        catch
        {
            // Data missing or malformed: the app boots without autocorrect rather
            // than failing. Nothing else in the app depends on it.
            _autocorrectEngine = null;
        }
    }

    // Idempotent settings → runtime reconciliation, called at boot and on every
    // settings flush. Tracks the started state so repeated reconciles (from an
    // unrelated module settings change) never re-Start the keyboard host.
    private void ReconcileAutocorrect()
    {
        if (_autocorrectEngine is null) return;

        bool shouldRun = AutocorrectSettingsService.Instance.Current.Enabled;
        if (shouldRun && !_autocorrectStarted)
        {
            _autocorrectStarted = _autocorrectEngine.Start();
        }
        else if (!shouldRun && _autocorrectStarted)
        {
            _autocorrectEngine.Stop();
            _autocorrectStarted = false;
        }
    }

    // Called from QuitApp. Dispose stops the keyboard host (an injected burst
    // must never outlive the process), then the dictionary flushes its state.
    private void ShutdownAutocorrect()
    {
        _autocorrectEngine?.Dispose();
        _autocorrectDictionary?.Dispose();
    }

    // Turns an enrollment suggestion into a toast and writes the user's answer.
    // Runs detached from the engine's input thread (fire-and-forget): blocking
    // that thread would freeze typing. A null answer (ignored, dropped, or the
    // toast expired) leaves the app undecided — it is offered again next run.
    private static async Task PromptAutocorrectEnrollmentAsync(string process)
    {
        var dispatcher = NotificationDispatcher.Instance;
        if (dispatcher is null) return; // boot wiring not done — nothing to ask through

        NotificationResponse? response;
        try
        {
            response = await dispatcher.PromptAsync(
                AutocorrectNotifications.Enroll, bodyArgs: new object?[] { process });
        }
        catch
        {
            // Channel failure (e.g. an elevated process drops toasts): best-effort,
            // leave the app undecided so a later run can offer it again.
            return;
        }

        if (response is null) return;

        bool enable = response.ActionId == AutocorrectNotifications.EnableAction;

        // The module owns the write: SetDecision swaps the decision map by
        // reference under its own lock, so the engine — reading Apps live on its
        // input thread — never observes a half-updated map.
        AutocorrectSettingsService.Instance.SetDecision(process, enable);
    }

    // The personal counterpart of the accent index: maps a folded key to the
    // user's adopted surface forms that fold to it. Rebuilt per lookup because
    // adoption shifts with the decay clock, not only with mutations (mirrors the
    // CLI run path).
    private static Func<string, IReadOnlyList<AccentVariant>> BuildAutocorrectPersonalVariants(
        PersonalDictionary dictionary)
    {
        return key =>
        {
            List<AccentVariant>? match = null;
            foreach (string word in dictionary.AdoptedWords)
            {
                if (AccentFolding.Fold(word) != key)
                    continue;
                (match ??= new List<AccentVariant>()).Add(new AccentVariant(word, double.MaxValue));
            }
            return match ?? (IReadOnlyList<AccentVariant>)Array.Empty<AccentVariant>();
        };
    }
}
