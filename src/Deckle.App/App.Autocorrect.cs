using System.IO;
using Deckle.Core;
using Deckle.Input.Autocorrect;
using Deckle.Input;

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

    private void InitializeAutocorrect()
    {
        try
        {
            string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
            string frenchPath = Path.Combine(dataDir, "lexicon-fr.tsv.gz");
            string pairPath = Path.Combine(dataDir, "pair-bigrams-fr.tsv.gz");

            // The French lexicon is the gate; without it there is nothing to do,
            // so leave autocorrect unbuilt rather than start a no-op engine.
            if (!File.Exists(frenchPath))
                return;

            var french = FrequencyLexicon.LoadTsvGz(frenchPath);
            var index = AccentIndex.Build(french);
            var context = File.Exists(pairPath)
                ? BigramPairDisambiguator.LoadTsvGz(pairPath, null)
                : null;

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
                host: new KeyboardInputHost(),
                decoder: new KeyDecoder(),
                tracker: new TypedWordTracker(),
                prober: new SurfaceProber(),
                policy: policy,
                injector: new TextInjector(),
                settings: () => AutocorrectSettingsService.Instance.Current,
                dictionary: _autocorrectDictionary,
                french: french,
                english: null);

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
