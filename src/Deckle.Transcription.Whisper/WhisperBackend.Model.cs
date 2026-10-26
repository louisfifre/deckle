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
            string modelName = Path.GetFileName(modelPath);

            if (!File.Exists(modelPath))
            {
                ReportLoadFailure(
                    "file_not_found", modelName, modelPath, 0, gpuRequested: false, device: "", failedDevice: "");
                return new ModelLoadResult(false, 0, null, "file_not_found");
            }

            var sw = Stopwatch.StartNew();
            IntPtr ctxParamsPtr = WhisperPInvoke.whisper_context_default_params_by_ref();
            WhisperContextParams ctxParams = Marshal.PtrToStructure<WhisperContextParams>(ctxParamsPtr);
            WhisperPInvoke.whisper_free_context_params(ctxParamsPtr);
            // Read from the live settings at each load, like the model choice:
            // a toggle on the page reaches whisper at the next load, after the
            // idle unload or the restart the page offers. A change does not
            // force an unload: the resident context may be serving a take, and
            // the restart already gives the user an immediate path.
            WhisperContextParamsMapper.Apply(ref ctxParams, _host.Transcription);

            bool gpuRequested = ctxParams.use_gpu != 0;
            bool logHookInstalled = Volatile.Read(ref s_logHookInstalled) != 0;

            // Ungated: what was asked and which runtime answers are the first
            // two questions of a log read on another machine.
            DeckleWhisperSource.Log.ModelLoadStarting(modelName, gpuRequested, s_runtimeVersion);
            DeckleWhisperSource.Log.ModelLoadStartingDetail(
                modelPath,
                FileSizeMb(modelPath),
                ctxParams.gpu_device,
                ctxParams.flash_attn != 0,
                logHookInstalled);

            // Reset before init: the log hook feeds the observer synchronously
            // during init, and a re-load must not keep the previous load's
            // device.
            s_computeDevice.BeginLoad();

            // Here and not in the constructor: loading the backends is where
            // ggml enumerates the GPU, which has no place at app startup.
            NativeRuntimeLoader.EnsureBackendsLoaded(AppPaths.NativeDirectory);

            _ctx = WhisperPInvoke.whisper_init_from_file_with_params(modelPath, ctxParams);
            sw.Stop();
            // The last init phase has no following line to flush it until the
            // first take; flushing here keeps the phase summary with its load.
            _logCompactor.FlushPendingPhase();
            DeckleWhispSource.Log.ModelInitFromFile((long)_ctx);

            if (_ctx == IntPtr.Zero)
            {
                ReportLoadFailure(
                    "init_failed",
                    modelName,
                    modelPath,
                    sw.ElapsedMilliseconds,
                    gpuRequested,
                    s_computeDevice.DeviceOfCurrentLoad(gpuRequested),
                    s_computeDevice.FailedGpuDevice);
                return new ModelLoadResult(false, sw.ElapsedMilliseconds, null, "init_failed");
            }

            _loadFailureOpen = false;
            _detectedBackend = s_computeDevice.ReportLoad(gpuRequested, logHookInstalled, sw.ElapsedMilliseconds);
            LogNativeSystemInfoOnce();

            return new ModelLoadResult(true, sw.ElapsedMilliseconds, _detectedBackend, null);
        }
    }

    // Runs under _modelLock. The engine frames the incident for the user with
    // ModelUnavailable/ModelRecovered from the returned ErrorReason; this
    // Verbose mirror carries the native facts (which file, which device) once
    // per incident.
    private void ReportLoadFailure(
        string reason,
        string modelName,
        string modelPath,
        long elapsedMs,
        bool gpuRequested,
        string device,
        string failedDevice)
    {
        if (!_loadFailureOpen)
        {
            _loadFailureOpen = true;
            DeckleWhisperSource.Log.ModelLoadFailed(reason, modelName, device);
        }
        DeckleWhisperSource.Log.ModelLoadFailedDetail(reason, modelPath, elapsedMs, gpuRequested, failedDevice);
    }

    // -1 when the size cannot be read: a log field must never fail the load
    // it describes.
    private static double FileSizeMb(string path)
    {
        try { return new FileInfo(path).Length / 1024.0 / 1024.0; }
        catch (Exception ex)
        {
            DeckleWhisperSource.Log.ModelFileProbeFailed(path, ex.GetType().Name, ex.Message);
            return -1;
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
            DeckleWhispSource.Log.ModelFallback(resolution.ConfiguredFileName, installed);
        }

        if (resolution.IgnoredEnvironmentPath is { } ignored)
        {
            DeckleWhispSource.Log.ModelPathEnvIgnored(Path.GetFileName(resolution.Path));
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
        _encoderBeginCallback = null;
    }
}
