using System;
using System.Threading.Tasks;
using Deckle.Autocorrect;

namespace Deckle.App;

// Autocorrect activation owns the expensive runtime as one resource. Disabled
// means no lexicons, inference session, background rerank lane or input-host
// reference remains alive. Settings callbacks can arrive on the debounce
// thread, so every transition is serialized with an in-flight build.
public partial class App
{
    private readonly object _autocorrectLifecycleLock = new();
    private AutocorrectRuntime? _autocorrectRuntime;
    private bool _autocorrectInitializing;
    private bool _autocorrectSubscribed;
    private bool _autocorrectShuttingDown;

    private sealed class AutocorrectRuntime : IDisposable
    {
        private bool _disposed;

        public AutocorrectRuntime(
            AutocorrectEngine engine,
            PersonalDictionary dictionary,
            string rerankerEngine,
            long rerankerLoadMs,
            string lexiconKey)
        {
            Engine = engine;
            Dictionary = dictionary;
            RerankerEngine = rerankerEngine;
            RerankerLoadMs = rerankerLoadMs;
            LexiconKey = lexiconKey;
        }

        public AutocorrectEngine Engine { get; }
        public PersonalDictionary Dictionary { get; }
        public string RerankerEngine { get; }
        public long RerankerLoadMs { get; }

        // Identifies the effective lexicon this engine was built over — the
        // active domain packs. The merge happens once at load, so a change here
        // cannot be applied in place: the runtime is torn down and rebuilt.
        public string LexiconKey { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Engine.Dispose(); }
            finally { Dictionary.Dispose(); }
        }
    }

    // Lightweight boot wiring only. ReconcileAutocorrect starts the expensive
    // build if and only if the persisted switch is currently on.
    private void InitializeAutocorrect()
    {
        lock (_autocorrectLifecycleLock)
        {
            if (_autocorrectSubscribed || _autocorrectShuttingDown) return;
            AutocorrectSettingsService.Instance.Changed += ReconcileAutocorrect;
            Deckle.Diagnostics.Telemetry.TelemetrySettingsService.Instance.Changed +=
                ReconcileAutocorrectTelemetry;
            _autocorrectSubscribed = true;
        }

        ReconcileAutocorrect();
    }

    private void ReconcileAutocorrect()
    {
        bool beginInitialization = false;

        lock (_autocorrectLifecycleLock)
        {
            if (_autocorrectShuttingDown) return;

            if (!AutocorrectSettingsService.Instance.Current.Enabled)
            {
                AutocorrectRuntime? runtime = _autocorrectRuntime;
                _autocorrectRuntime = null;
                runtime?.Dispose();
                return;
            }

            // A settings change that alters the effective lexicon — a domain
            // pack turned on or off — cannot be reconciled in place: the merged
            // table, the accent index and every policy built over them are
            // bound at construction. Drop the runtime and let the branch below
            // rebuild it. Enrolling an app, by contrast, leaves the key equal
            // and the engine reads the decision map live, so nothing is torn
            // down for it.
            //
            // Settings writes reach here debounced, so a burst of edits costs
            // one rebuild, not one per edit. The rebuild is the same cost as
            // flipping the master switch off and on — it reloads the sentence
            // model too.
            AutocorrectRuntime? stale = _autocorrectRuntime;
            if (stale is not null
                && stale.LexiconKey != AutocorrectSettings.EffectiveLexiconKey(
                    AutocorrectSettingsService.Instance.Current))
            {
                _autocorrectRuntime = null;
                stale.Dispose();
            }

            if (_autocorrectRuntime is null && !_autocorrectInitializing)
            {
                _autocorrectInitializing = true;
                beginInitialization = true;
            }
        }

        if (beginInitialization)
            _ = BuildAndActivateAutocorrectAsync();
    }

    private async Task BuildAndActivateAutocorrectAsync()
    {
        AutocorrectRuntime? runtime = null;
        try
        {
            runtime = await BuildAutocorrectRuntimeAsync().ConfigureAwait(false);
            bool retained = false;

            lock (_autocorrectLifecycleLock)
            {
                _autocorrectInitializing = false;

                // The switch may have turned off while the model was loading.
                // In that case the just-built runtime is discarded immediately;
                // it never takes a shared input-host reference.
                if (!_autocorrectShuttingDown
                    && AutocorrectSettingsService.Instance.Current.Enabled
                    && runtime is not null
                    && runtime.Engine.Start())
                {
                    DeckleAutocorrectSource.Log.RerankerStatus(
                        runtime.RerankerEngine, runtime.RerankerLoadMs);
                    DeckleAutocorrectSource.Log.EngineReady();
                    runtime.Engine.CorrectionApplied += OnParagraphCorrectionApplied;
                    _autocorrectRuntime = runtime;
                    retained = true;
                }
            }

            if (!retained)
                runtime?.Dispose();
            else
            {
                // A pack may have been flipped while this build ran, leaving the
                // engine just published reading the previous table. One more
                // pass settles it; the key comparison makes it a no-op unless
                // that actually happened.
                ReconcileAutocorrect();
            }
        }
        catch
        {
            try { runtime?.Dispose(); }
            catch { /* Best-effort cleanup on an already failed activation. */ }
            finally
            {
                lock (_autocorrectLifecycleLock)
                    _autocorrectInitializing = false;
            }
        }
    }

    private void ReconcileAutocorrectTelemetry()
    {
        lock (_autocorrectLifecycleLock)
        {
            if (!_autocorrectShuttingDown)
                _autocorrectRuntime?.Engine.ReconcileTextTelemetry();
        }
    }

    // Called from QuitApp. An in-flight build observes the shutdown flag when
    // it completes and disposes its local runtime instead of publishing it.
    private void ShutdownAutocorrect()
    {
        lock (_autocorrectLifecycleLock)
        {
            if (_autocorrectShuttingDown) return;
            _autocorrectShuttingDown = true;

            if (_autocorrectSubscribed)
            {
                AutocorrectSettingsService.Instance.Changed -= ReconcileAutocorrect;
                Deckle.Diagnostics.Telemetry.TelemetrySettingsService.Instance.Changed -=
                    ReconcileAutocorrectTelemetry;
                _autocorrectSubscribed = false;
            }

            AutocorrectRuntime? runtime = _autocorrectRuntime;
            _autocorrectRuntime = null;
            runtime?.Dispose();
        }
    }
}
