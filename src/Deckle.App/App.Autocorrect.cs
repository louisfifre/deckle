using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Deckle.Core;
using Deckle.Autocorrect;
using Deckle.Autocorrect.Mlm;
using Deckle.Autocorrect.Onnx;
using Deckle.Input;
using Deckle.Notifications;

namespace Deckle.App;

// Autocorrect module composition — same posture as App.Trackpad: the App owns
// the engine and reconciles it with the persisted module settings. The engine
// chains the diacritics restorer (lexical gate + bigram left-context) and a
// conservative typo corrector (non-words → nearest common French word), wired
// to the real keyboard, repairing words on enrolled surfaces. Enabled by
// default (AutocorrectSettings); corrections land only on enrolled processes
// (Notepad out of the box) and never on a password surface.
//
// The live post-sentence stage resolves real-word ambiguities — la/là, a/à,
// ou/où — plus sentence-initial capitals. It starts with deterministic French
// rules and delegates to a model engine when one is present, by preference:
// the ONNX GenAI sentence judge (models\sentence-judge\), else the CamemBERT
// masked-LM (models\camembert-base\). Both live under the user data root,
// staged by the maintainer, never shipped in the build.
public partial class App
{
    // Contextual reranker calibration — mirrors the offline EvaluateReranked
    // operating point: act only on a clear top-vs-second logit gap, and prefer the
    // common form. Starting points to ground by the eval, not measured optima.
    private const double RerankerMargin = 2.0;
    private const double RerankerFreqPrior = 1.0;

    // Sentence judge operating margin, maintainer-decided on the 2026-07 replay
    // calibration (979 slots, maintainer truth overlaid): 1.0 holds 92.2%
    // precision at 20.8% coverage, against 90.8%/41.0% at 0.5. Chosen on the
    // precision side for the live start — an abstention is invisible, a wrong
    // change erodes trust — to be relaxed as the widened corpus grows. Margins
    // are per-export: this one is calibrated on the DML int4 export the live
    // path loads, and must be recalibrated if the export changes.
    private const double SentenceJudgeMargin = 1.0;

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
            string frenchPath = Path.Combine(dataDir, AutocorrectLexiconArtifacts.FrenchFileName);
            string pairPath = Path.Combine(dataDir, AutocorrectLexiconArtifacts.PairBigramsFrenchFileName);
            string verbsPath = Path.Combine(dataDir, AutocorrectLexiconArtifacts.VerbMorphologyFrenchFileName);

            // The French lexicon is the gate; without it there is nothing to do,
            // so leave autocorrect unbuilt rather than start a no-op engine.
            if (!File.Exists(frenchPath))
                return;

            // The heavy step: gzip decode + build of the FR frequency lexicon,
            // its accent index, and the pair bigram model. Pure CPU/IO, no UI
            // affinity — run it on the thread pool so boot is not blocked.
            // Stopwatch wraps only the off-thread build; the elapsed ms lands
            // on the verbose LexiconLoadComplete (whisper's ModelLoadComplete
            // shape).
            var loadStopwatch = Stopwatch.StartNew();
            var (french, english, index, context, reranker, rerankerEngine, rerankerLoadMs, verbs) = await Task.Run(() =>
            {
                var fr = FrequencyLexicon.LoadTsvGz(frenchPath);
                var en = AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(dataDir);
                var idx = AccentIndex.Build(fr);
                var ctx = File.Exists(pairPath)
                    ? BigramPairDisambiguator.LoadTsvGz(pairPath, null)
                    : null;
                // Verb morphology drives the grammar stage (subject–verb
                // agreement). Optional like the pair model — absent its artifact
                // the engine simply runs the chain without agreement correction.
                var vb = File.Exists(verbsPath) ? VerbMorphology.LoadTsvGz(verbsPath) : null;
                // The sentence-stage engine, by preference order: the ONNX GenAI
                // sentence judge (Qwen3 DML int4, ~1.3 GB, resident on the GPU)
                // when its model directory is present, else the CamemBERT
                // masked-LM (~440 MB), else deterministic rules only. Both loads
                // are optional TryLoads off the UI thread; the winner and its
                // load cost go to RerankerStatus below.
                var rerankerStopwatch = Stopwatch.StartNew();
                string judgeDir = Path.Combine(AppPaths.ModelsDirectory, "sentence-judge");
                ISentenceReranker? rr = OnnxSlotReranker.TryLoad(judgeDir, margin: SentenceJudgeMargin);
                string engine = DeckleAutocorrectSource.RerankerEngines.SentenceJudge;
                if (rr is null)
                {
                    string modelDir = Path.Combine(AppPaths.ModelsDirectory, CamembertAssets.DirectoryName);
                    rr = CamembertReranker.TryLoad(
                        modelDir, margin: RerankerMargin, freqPrior: RerankerFreqPrior);
                    engine = rr is null
                        ? DeckleAutocorrectSource.RerankerEngines.None
                        : DeckleAutocorrectSource.RerankerEngines.Camembert;
                }
                rerankerStopwatch.Stop();
                return (fr, en, idx, ctx, rr, engine, rerankerStopwatch.ElapsedMilliseconds, vb);
            }).ConfigureAwait(true);
            loadStopwatch.Stop();
            DeckleAutocorrectSource.Log.LexiconLoadComplete(
                loadStopwatch.ElapsedMilliseconds - rerankerLoadMs, french.Count);

            // The only persisted text in the module — under the user data root,
            // inspectable and removable through the CLI `dict` command.
            string dictPath = Path.Combine(
                AppPaths.GetModuleDirectory("autocorrect"), "personal-dictionary.json");
            _autocorrectDictionary = new PersonalDictionary(dictPath);

            // The global-English tier is deliberately restricted: only the
            // globish seed artifact activates it. The historical full English
            // list is never loaded into the live protected-literal chain.
            var diacritics = new DiacriticsRestorer(
                french: french,
                english: english,
                index: index,
                options: new RestorerOptions(),
                context: context,
                personal: _autocorrectDictionary,
                personalVariants: BuildAutocorrectPersonalVariants(_autocorrectDictionary));

            // Stage two: Android-style spell-fix for true non-words the gate
            // leaves untouched ("bonjuor" → "bonjour"). Disjoint from diacritics
            // by construction; the composite makes the precedence explicit.
            var typo = new ConservativeTypoCorrector(
                french: french,
                english: english,
                personal: _autocorrectDictionary,
                options: new TypoOptions());

            // Stage two-bis, ahead of the typo corrector: restore a dropped elision
            // apostrophe in a glued proclitic ("cest" → "c'est", "jai" → "j'ai").
            // It must precede the typo corrector, which would otherwise rewrite
            // "cest" to "est" by a plain edit before the apostrophe is considered.
            var elision = new ElisionCorrector(french, english, _autocorrectDictionary);

            // Stage three: subject–verb agreement on a valid-but-misconjugated
            // word the stages above leave alone ("tu mange" → "tu manges").
            // Present only when the verb-morphology artifact loaded; last in the
            // chain, since it acts on the forms the earlier stages pass through.
            var grammar = verbs is not null
                ? new GrammarCorrector(verbs, _autocorrectDictionary)
                : null;

            var policies = new List<ICorrectionPolicy> { diacritics, elision, typo };
            if (grammar is not null)
                policies.Add(grammar);
            var policy = new CompositeCorrectionPolicy(policies.ToArray());

            var sentenceReranker = new FrenchSentenceReranker(reranker);

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
                english: english,
                // The post-sentence stage: deterministic French sentence rules,
                // delegating to the loaded model engine (sentence judge, else
                // CamemBERT) when one is present; the diacritics gate is reused
                // as the slot probe.
                reranker: sentenceReranker,
                probe: diacritics,
                // Opt-in per-word decision telemetry, read live so a Settings flip
                // takes effect without a rebuild. Off by default.
                decisionTelemetry: () =>
                    Deckle.Diagnostics.Telemetry.TelemetrySettingsService.Instance.Current.AutocorrectDecisions,
                // Opt-in typed-sentence corpus, same live read. Off by default; the
                // heaviest text capture, behind its own consent toggle.
                textTelemetry: () =>
                    Deckle.Diagnostics.Telemetry.TelemetrySettingsService.Instance.Current.AutocorrectText);

            // Reactive enrollment: a would-be correction on an undecided app
            // raises this on the engine's input thread. Detach the prompt so we
            // never block that thread; the user's answer writes the decision back.
            _autocorrectEngine.EnrollmentSuggested += p => _ = PromptAutocorrectEnrollmentAsync(p);

            AutocorrectSettingsService.Instance.Changed += ReconcileAutocorrect;
            Deckle.Diagnostics.Telemetry.TelemetrySettingsService.Instance.Changed +=
                ReconcileAutocorrectTelemetry;
            ReconcileAutocorrect();

            DeckleAutocorrectSource.Log.RerankerStatus(rerankerEngine, rerankerLoadMs);

            // Readiness edge: engine built, wired and reconciled. Concise
            // milestone, no number — the timing is on LexiconLoadComplete above.
            DeckleAutocorrectSource.Log.EngineReady();
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

    private void ReconcileAutocorrectTelemetry() =>
        _autocorrectEngine?.ReconcileTextTelemetry();

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
