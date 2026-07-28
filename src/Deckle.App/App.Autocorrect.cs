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
// chains elision repair, unified physical-typo/morphological evidence, the
// diacritics restorer (lexical gate + n-gram left context), then bounded grammar,
// wired to the real keyboard and repairing words on enrolled surfaces. Enabled by
// default (AutocorrectSettings); corrections land only on enrolled processes
// (Notepad out of the box) and never on a password surface.
//
// The closed-sentence stage resolves real-word ambiguities — la/là, a/à,
// ou/où — only after terminal punctuation. It starts with deterministic French
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

    // Builds the autocorrect runtime off the UI thread. This method is reached
    // only after the module switch turns on; a disabled module never opens the
    // lexicons and, crucially, never maps the optional GPU reranker.
    private async Task<AutocorrectRuntime?> BuildAutocorrectRuntimeAsync()
    {
        AutocorrectEngine? engine = null;
        PersonalDictionary? dictionary = null;
        FrenchSentenceReranker? sentenceReranker = null;
        ISentenceReranker? loadedReranker = null;

        try
        {
            // The keyboard/mouse Raw Input host is the process-shared one,
            // created by InitializeInputHost ahead of this. Without it there
            // is no input source to drive the engine.
            var host = _keyboardMouseHost;
            if (host is null) return null;

            string dataDir = AutocorrectLexiconArtifacts.DataDirectory;
            string frenchPath = Path.Combine(dataDir, AutocorrectLexiconArtifacts.FrenchFileName);
            string pairPath = Path.Combine(dataDir, AutocorrectLexiconArtifacts.PairBigramsFrenchFileName);
            string verbsPath = Path.Combine(dataDir, AutocorrectLexiconArtifacts.VerbMorphologyFrenchFileName);

            // The French lexicon is the gate; without it there is nothing to do,
            // so leave autocorrect unbuilt rather than start a no-op engine.
            if (!File.Exists(frenchPath))
                return null;

            // Which domain packs this engine will read, decided once here. The
            // key travels with the runtime so ReconcileAutocorrect can tell a
            // settings change that leaves the lexicon alone (an app enrolled)
            // from one that invalidates it (a pack flipped) — the merge happens
            // at load, so the second needs a rebuild and the first does not.
            var autocorrectSettings = AutocorrectSettingsService.Instance.Current;
            string lexiconKey = AutocorrectSettings.EffectiveLexiconKey(autocorrectSettings);
            IReadOnlyList<DomainPack> activePacks = DomainPack.ActiveIn(autocorrectSettings);
            // Snapshot the register by reference alongside the packs: the
            // settings service swaps the list rather than mutating it, so this
            // stays the exact set the key above describes.
            IReadOnlyCollection<string> excludedWords = autocorrectSettings.ExcludedWords;

            // The heavy step: gzip decode + build of the FR frequency lexicon,
            // its accent index, and the pair bigram model. Pure CPU/IO, no UI
            // affinity — run it on the thread pool so boot is not blocked.
            // Stopwatch wraps only the off-thread build; the elapsed ms lands
            // on the verbose LexiconLoadComplete (whisper's ModelLoadComplete
            // shape).
            var loadStopwatch = Stopwatch.StartNew();
            var (french, english, index, context, reranker, rerankerEngine, rerankerLoadMs, verbs) = await Task.Run(() =>
            {
                var fr = ComposeEffectiveLexicon(frenchPath, dataDir, activePacks, excludedWords);
                var en = new GlobalEnglishLexicon(
                    AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(dataDir));
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
                ISentenceReranker? rr = OnnxSlotReranker.TryLoad(
                    judgeDir,
                    margin: SentenceJudgeMargin,
                    executionProvider: "dml",
                    out Exception? judgeLoadError);
                string engine = DeckleAutocorrectSource.RerankerEngines.SentenceJudge;
                if (rr is null)
                {
                    if (judgeLoadError is not null)
                    {
                        DeckleAutocorrectSource.Log.RerankerLoadFailed(
                            DeckleAutocorrectSource.RerankerEngines.SentenceJudge,
                            judgeLoadError.ToString());
                    }

                    string modelDir = Path.Combine(AppPaths.ModelsDirectory, CamembertAssets.DirectoryName);
                    rr = CamembertReranker.TryLoad(
                        modelDir, margin: RerankerMargin, freqPrior: RerankerFreqPrior);
                    engine = rr is null
                        ? DeckleAutocorrectSource.RerankerEngines.None
                        : DeckleAutocorrectSource.RerankerEngines.Camembert;
                }
                rerankerStopwatch.Stop();
                return (fr, en, idx, ctx, rr, engine, rerankerStopwatch.ElapsedMilliseconds, vb);
            }).ConfigureAwait(false);
            loadedReranker = reranker;
            loadStopwatch.Stop();
            DeckleAutocorrectSource.Log.LexiconLoadComplete(
                loadStopwatch.ElapsedMilliseconds - rerankerLoadMs, french.Count);

            // The only persisted text in the module — under the user data root,
            // inspectable and removable through the CLI `dict` command.
            string dictPath = Path.Combine(
                AppPaths.GetModuleDirectory("autocorrect"), "personal-dictionary.json");
            var personalWordAdmission = new PersonalWordAdmission(french, index, english);
            dictionary = new PersonalDictionary(
                dictPath,
                wordAdmission: personalWordAdmission.Allows);
            if (dictionary.RemovedOnLoad > 0)
                DeckleAutocorrectSource.Log.PersonalDictionarySanitized(dictionary.RemovedOnLoad);

            // Approved mistouch families — per-user data beside the dictionary,
            // same discipline (inspectable, editable, removable). The kinds are
            // code; these records are the user's own mined-and-reviewed slips.
            var mistouchFamilies = MistouchFamilyStore.Load(Path.Combine(
                AppPaths.GetModuleDirectory("autocorrect"), MistouchFamilyStore.FileName));

            // One shared production composition: the same policy order and
            // optional lexical knowledge are exercised by the quality tests.
            AutocorrectPolicySet policies = AutocorrectPolicySet.Create(
                french,
                english,
                index,
                context,
                dictionary,
                BuildAutocorrectPersonalVariants(dictionary),
                verbs);

            sentenceReranker = new FrenchSentenceReranker(loadedReranker);
            loadedReranker = null; // ownership moved into the wrapper

            engine = new AutocorrectEngine(
                host: host,
                decoder: new KeyDecoder(),
                tracker: new TypedWordTracker(),
                prober: new SurfaceProber(),
                policy: policies.Policy,
                injector: new TextInjector(),
                settings: () => AutocorrectSettingsService.Instance.Current,
                dictionary: dictionary,
                french: french,
                english: english,
                // The post-sentence stage: deterministic French sentence rules,
                // delegating to the loaded model engine (sentence judge, else
                // CamemBERT) when one is present. The slot probe merges unresolved
                // accent variants with bounded typo neighbours; both preserve the
                // exact literal as an explicit KEEP candidate.
                reranker: sentenceReranker,
                probe: policies.AmbiguityProbe,
                // Opt-in per-word decision telemetry, read live so a Settings flip
                // takes effect without a rebuild. Off by default.
                decisionTelemetry: () =>
                    Deckle.Diagnostics.Telemetry.TelemetrySettingsService.Instance.Current.AutocorrectDecisions,
                // Opt-in typed-sentence corpus, same live read. Off by default; the
                // heaviest text capture, behind its own consent toggle.
                textTelemetry: () =>
                    Deckle.Diagnostics.Telemetry.TelemetrySettingsService.Instance.Current.AutocorrectText,
                mistouchFamilies: mistouchFamilies);
            sentenceReranker = null; // ownership moved into the engine's lane

            // Reactive enrollment: a would-be correction on an undecided app
            // raises this on the engine's input thread. Detach the prompt so we
            // never block that thread; the user's answer writes the decision back.
            engine.EnrollmentSuggested += p => _ = PromptAutocorrectEnrollmentAsync(p);

            var runtime = new AutocorrectRuntime(
                engine, dictionary, rerankerEngine, rerankerLoadMs, lexiconKey);
            engine = null;
            dictionary = null;
            return runtime;
        }
        catch
        {
            // Data missing or malformed: the app continues without autocorrect
            // rather than failing. Nothing else in the app depends on it.
            try
            {
                engine?.Dispose();
                sentenceReranker?.Dispose();
                (loadedReranker as IDisposable)?.Dispose();
            }
            finally
            {
                dictionary?.Dispose();
            }
            return null;
        }
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

    // Builds the effective lexicon — the single table the correctors consult:
    // the base French lexicon fused with the active packs, highest frequency
    // winning on a form both carry, then the user's excluded words subtracted
    // (CONTEXT.md § Lexicon composition, § Word exclusion). Runs inside the
    // off-UI-thread load with the rest of the lexical build.
    //
    // A pack whose artifact is missing from the build is skipped rather than
    // failing the load: a pack extends the lexicon, it is never a prerequisite
    // for correcting French.
    private static FrequencyLexicon ComposeEffectiveLexicon(
        string frenchPath,
        string dataDir,
        IReadOnlyList<DomainPack> activePacks,
        IReadOnlyCollection<string> excludedWords)
    {
        var baseLexicon = FrequencyLexicon.LoadTsvGz(frenchPath);

        var packLexicons = new List<FrequencyLexicon>(activePacks.Count);
        foreach (DomainPack pack in activePacks)
        {
            FrequencyLexicon? forms = pack.TryLoad(dataDir);
            if (forms is null) continue;
            DeckleAutocorrectSource.Log.DomainPackMerged(pack.Id, forms.Count);
            packLexicons.Add(forms);
        }

        return EffectiveLexicon.Compose(baseLexicon, packLexicons, excludedWords);
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
