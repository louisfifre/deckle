using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Deckle.Core;
using Deckle.Autocorrect;
using Deckle.Input;
using Deckle.Notifications;

namespace Deckle.App;

public partial class App
{
    // Reached only once the module switch is on: a disabled module never opens
    // the lexicons.
    private async Task<AutocorrectRuntime?> BuildAutocorrectRuntimeAsync()
    {
        AutocorrectEngine? engine = null;
        PersonalDictionary? dictionary = null;

        try
        {
            // The process-shared Raw Input host, which InitializeInputHost creates first.
            var host = _keyboardMouseHost;
            if (host is null)
            {
                DeckleAppSource.Log.AutocorrectUnavailable("input_host_missing");
                return null;
            }

            string dataDir = AutocorrectLexiconArtifacts.DataDirectory;
            string frenchPath = Path.Combine(dataDir, AutocorrectLexiconArtifacts.FrenchFileName);
            string pairPath = Path.Combine(dataDir, AutocorrectLexiconArtifacts.PairBigramsFrenchFileName);
            string verbsPath = Path.Combine(dataDir, AutocorrectLexiconArtifacts.VerbMorphologyFrenchFileName);

            if (!File.Exists(frenchPath))
            {
                DeckleAppSource.Log.AutocorrectUnavailable("lexicon_missing");
                return null;
            }

            var autocorrectSettings = AutocorrectSettingsService.Instance.Current;
            var systemLanguages = SystemLanguages.Current;
            string lexiconKey =
                DomainActivation.EffectiveLexiconKey(autocorrectSettings, systemLanguages);
            IReadOnlyList<DomainPack> activePacks =
                DomainActivation.ActiveIn(autocorrectSettings, systemLanguages);
            // Snapshot the register by reference alongside the packs: the
            // settings service swaps the list rather than mutating it, so this
            // stays the exact set the key above describes.
            IReadOnlyCollection<string> excludedWords = autocorrectSettings.ExcludedWords;

            // The heavy step is pure CPU and IO with no UI affinity, so it runs on
            // the thread pool.
            var loadStopwatch = Stopwatch.StartNew();
            var (french, english, index, context, verbs) = await Task.Run(() =>
            {
                var fr = ComposeEffectiveLexicon(frenchPath, dataDir, activePacks, excludedWords);
                var en = new GlobalEnglishLexicon(
                    AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(dataDir));
                var idx = AccentIndex.Build(fr);
                var ctx = File.Exists(pairPath)
                    ? BigramPairDisambiguator.LoadTsvGz(pairPath, null)
                    : null;
                if (ctx is null)
                    DeckleAppSource.Log.AutocorrectAssetSkipped("pair_bigrams", "file_missing");
                // Without verb morphology the engine runs without subject-verb
                // agreement correction.
                var vb = File.Exists(verbsPath) ? VerbMorphology.LoadTsvGz(verbsPath) : null;
                if (vb is null)
                    DeckleAppSource.Log.AutocorrectAssetSkipped("verb_morphology", "file_missing");
                return (fr, en, idx, ctx, vb);
            }).ConfigureAwait(false);
            loadStopwatch.Stop();
            DeckleAutocorrectSource.Log.LexiconLoadComplete(
                loadStopwatch.ElapsedMilliseconds, french.Count);

            // Persisted text, kept under the user data root: inspectable and removable.
            string dictPath = Path.Combine(
                AppPaths.GetModuleDirectory("autocorrect"), "personal-dictionary.json");
            var personalWordAdmission = new PersonalWordAdmission(french, index, english);
            dictionary = new PersonalDictionary(
                dictPath,
                wordAdmission: personalWordAdmission.Allows);
            if (dictionary.RemovedOnLoad > 0)
                DeckleAutocorrectSource.Log.PersonalDictionarySanitized(dictionary.RemovedOnLoad);

            // Per-user records beside the dictionary, under the same discipline.
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
                // Read live so a Settings flip takes effect without a rebuild.
                decisionTelemetry: () =>
                    Deckle.Diagnostics.Telemetry.TelemetrySettingsService.Instance.Current.AutocorrectDecisions,
                // The heaviest text capture, behind its own consent toggle.
                textTelemetry: () =>
                    Deckle.Diagnostics.Telemetry.TelemetrySettingsService.Instance.Current.AutocorrectText,
                mistouchFamilies: mistouchFamilies);

            // Raised on the engine's input thread: the prompt is detached so it
            // never blocks typing.
            engine.EnrollmentSuggested += p => _ = PromptAutocorrectEnrollmentAsync(p);

            var runtime = new AutocorrectRuntime(engine, dictionary, lexiconKey);
            engine = null;
            dictionary = null;
            return runtime;
        }
        catch (Exception ex)
        {
            // Data missing or malformed: the app continues without autocorrect
            // rather than failing. Nothing else in the app depends on it.
            DeckleAppSource.Log.AutocorrectFailed("build", ex.GetType().Name, ex.Message);
            try
            {
                engine?.Dispose();
            }
            finally
            {
                dictionary?.Dispose();
            }
            return null;
        }
    }

    // A null answer (ignored, dropped, or the toast expired) leaves the app
    // undecided: it is offered again next run.
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
        catch (Exception ex)
        {
            // Channel failure (e.g. an elevated process drops toasts): best-effort,
            // leave the app undecided so a later run can offer it again.
            DeckleAppSource.Log.AutocorrectEnrollmentPromptFailed(ex.GetType().Name, ex.Message);
            return;
        }

        if (response is null) return;

        bool enable = response.ActionId == AutocorrectNotifications.EnableAction;

        // The module owns the write: SetDecision swaps the decision map by
        // reference under its own lock, so the engine — reading Apps live on its
        // input thread — never observes a half-updated map.
        AutocorrectSettingsService.Instance.SetDecision(process, enable);
    }

    // The single table the correctors consult. A pack whose artifact is missing
    // is skipped rather than failing the load: a pack extends the lexicon, it is
    // never a prerequisite for correcting French.
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
            if (forms is null)
            {
                // The person switched this pack on; without its file the
                // corrections fall back to the base lexicon for its domain.
                DeckleAppSource.Log.AutocorrectAssetSkipped($"pack:{pack.Id}", "file_missing");
                continue;
            }
            DeckleAutocorrectSource.Log.DomainPackMerged(pack.Id, forms.Count);
            packLexicons.Add(forms);
        }

        return EffectiveLexicon.Compose(baseLexicon, packLexicons, excludedWords);
    }

    // The personal counterpart of the accent index. Rebuilt per lookup because
    // adoption shifts with the decay clock, not only with mutations.
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
