using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.Transcription.Whisper;

public sealed partial class WhisperBackend
{
    public Task<ModelLoadResult> LoadModelAsync(CancellationToken ct)
    {
        // whisper_init_from_file_with_params blocks; the async signature is the
        // IAsrBackend contract.
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(LoadModelSync());
    }

    private ModelLoadResult LoadModelSync()
    {
        lock (_modelLock)
        {
            if (_ctx != IntPtr.Zero)
            {
                // Already loaded: success with a zero load time keeps the call
                // idempotent.
                return new ModelLoadResult(true, 0, _detectedBackend, null);
            }

            string modelPath = ResolveModelPath();

            if (!File.Exists(modelPath))
            {
                return new ModelLoadResult(false, 0, null, "file_not_found");
            }

            if (OperationalLogAdmission.IsDetailEnabled(
                    OperationalLogActivity.Transcription,
                    DeckleWhispSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Lifecycle))
            {
                double fileMb = new FileInfo(modelPath).Length / 1024.0 / 1024.0;
                string basename = Path.GetFileName(modelPath);
                DeckleWhispSource.Log.ModelLoadStart(basename, fileMb);
            }

            // Reset before init: the log hook overwrites this field
            // synchronously during init when it sees a GPU backend prefix, and
            // a re-load must not keep the previous load's backend.
            _detectedBackend = "CPU";

            var sw = Stopwatch.StartNew();
            IntPtr ctxParamsPtr = WhisperPInvoke.whisper_context_default_params_by_ref();
            WhisperContextParams ctxParams = Marshal.PtrToStructure<WhisperContextParams>(ctxParamsPtr);
            WhisperPInvoke.whisper_free_context_params(ctxParamsPtr);
            ctxParams.use_gpu = 1;

            _ctx = WhisperPInvoke.whisper_init_from_file_with_params(modelPath, ctxParams);
            sw.Stop();
            DeckleWhispSource.Log.ModelInitFromFile((long)_ctx);

            if (_ctx == IntPtr.Zero)
            {
                return new ModelLoadResult(false, sw.ElapsedMilliseconds, null, "init_failed");
            }

            DeckleWhispSource.Log.ModelLoadComplete(sw.ElapsedMilliseconds, _detectedBackend);

            return new ModelLoadResult(true, sw.ElapsedMilliseconds, _detectedBackend, null);
        }
    }

    public void UnloadModel()
    {
        lock (_modelLock)
        {
            if (_ctx == IntPtr.Zero) return;
            WhisperPInvoke.whisper_free(_ctx);
            _ctx = IntPtr.Zero;
            DeckleWhispSource.Log.ModelUnloadedJalon();
        }
    }

    // Order of precedence:
    //   1. DECKLE_MODEL_PATH env var if it points to an absolute existing path.
    //   2. Engine.Model (user setting), fallback to the Whisper catalog's
    //      default, joined with host.ResolveModelsDirectory().
    //   3. If that file is absent, the best catalog model actually installed,
    //      so an install carrying only large-v3 keeps working without a 3 GB
    //      re-download.
    private string ResolveModelPath()
    {
        string modelsDirectory = _host.ResolveModelsDirectory();
        string? envPath = Environment.GetEnvironmentVariable("DECKLE_MODEL_PATH");
        SpeechModelResolution resolution = SpeechModelResolver.ResolvePath(
            _host.Transcription.Engine.Model,
            modelsDirectory,
            envPath,
            SpeechModels.IsUsableModelFile);

        if (resolution.InstalledFallbackFileName is { } installed)
        {
            DeckleWhispSource.Log.ModelFallback();
            DeckleWhispSource.Log.ModelFallbackDetail(
                resolution.ConfiguredFileName,
                installed);
        }

        if (resolution.IgnoredEnvironmentPath is { } ignored)
        {
            DeckleWhispSource.Log.ModelPathEnvIgnored();
            DeckleWhispSource.Log.ModelPathEnvIgnoredDetail(ignored, resolution.Path);
        }
        return resolution.Path;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnloadModel();
        // The process-global log thunk stays statically rooted; its weak owner
        // disappears once this backend is no longer referenced.
        _segmentCallback = null;
        _abortCallback = null;
    }
}
