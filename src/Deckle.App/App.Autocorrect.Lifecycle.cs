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
            long rerankerLoadMs)
        {
            Engine = engine;
            Dictionary = dictionary;
            RerankerEngine = rerankerEngine;
            RerankerLoadMs = rerankerLoadMs;
        }

        public AutocorrectEngine Engine { get; }
        public PersonalDictionary Dictionary { get; }
        public string RerankerEngine { get; }
        public long RerankerLoadMs { get; }

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
