using System.Diagnostics;
using Deckle.Diagnostics;

namespace Deckle.Transcription.Parakeet;

public sealed partial class ParakeetBackend
{
    public Task<ModelLoadResult> LoadModelAsync(CancellationToken ct)
    {
        // Opening the sessions blocks; the async signature is the IAsrBackend contract.
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(LoadModelSync());
    }

    private ModelLoadResult LoadModelSync()
    {
        lock (_modelLock)
        {
            if (_model is { } resident)
            {
                // Already loaded: success with a zero load time keeps the call idempotent.
                return new ModelLoadResult(true, 0, resident.Accelerator, null);
            }

            // Read from the live settings at each load, like the model choice: a change reaches
            // Parakeet at the next load, after the idle unload or a restart.
            EngineSettings engine = _host.Transcription.Engine;
            ParakeetModelFiles files = ParakeetModelFiles.Resolve(engine.ParakeetModelDirectory, _host.ResolveModelsDirectory());
            ParakeetEncoderPrecision precision = engine.ParakeetEncoder;
            bool useGpu = engine.UseGpu;
            string modelName = Path.GetFileName(files.Directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string precisionName = PrecisionName(precision);
            string encoderPath = precision == ParakeetEncoderPrecision.Fp16 ? files.EncoderFp16 : files.EncoderFp32;

            // Ungated: what was asked is the first question of a log read on another machine.
            DeckleParakeetSource.Log.ModelLoadStarting(modelName, precisionName, useGpu);
            DeckleParakeetSource.Log.ModelLoadStartingDetail(files.Directory, FileSizeMb(encoderPath));

            var sw = Stopwatch.StartNew();
            ParakeetLoadOutcome outcome = _loader.Load(files, precision, useGpu);
            sw.Stop();

            if (outcome.Model is not { } model)
            {
                string reason = outcome.ErrorReason ?? "init_failed";
                string failedPath = outcome.FailedPath ?? files.Directory;
                if (!_loadFailureOpen)
                {
                    _loadFailureOpen = true;
                    DeckleParakeetSource.Log.ModelLoadFailed(reason, modelName);
                }
                DeckleParakeetSource.Log.ModelLoadFailedDetail(
                    reason, failedPath, sw.ElapsedMilliseconds, useGpu, outcome.ExceptionType, outcome.ExceptionMessage);
                return new ModelLoadResult(false, sw.ElapsedMilliseconds, null, reason, failedPath, useGpu);
            }

            _model = model;
            _loadFailureOpen = false;
            ReportDevice(model, useGpu, precisionName);
            DeckleParakeetSource.Log.ModelLoadComplete(
                sw.ElapsedMilliseconds, model.Accelerator, precisionName, useGpu, model.GpuFallbackExceptionType ?? "");
            return new ModelLoadResult(true, sw.ElapsedMilliseconds, model.Accelerator, null);
        }
    }

    // What the load obtained against what the settings asked: a fallback is a Warning that
    // names both, once per incident, and the next load that gets what it asked closes it.
    private void ReportDevice(ParakeetLoadedModel model, bool useGpu, string precisionName)
    {
        if (model.GpuFallbackExceptionType is { } type)
        {
            if (!_gpuFallbackOpen)
            {
                _gpuFallbackOpen = true;
                DeckleParakeetSource.Log.ModelGpuUnavailable("device_unavailable", type);
            }
            return;
        }

        _gpuFallbackOpen = false;
        if (useGpu && model.Accelerator == ParakeetAccelerators.DirectMl)
            DeckleParakeetSource.Log.ModelOnGpu("directml_0", precisionName);
        else
            DeckleParakeetSource.Log.ModelOnCpu(precisionName);
    }

    public void UnloadModel()
    {
        lock (_modelLock)
        {
            // Waits for a running transcription: freeing sessions under it would crash the process.
            lock (_transcribeLock)
            {
                if (_model is not { } model) return;
                _model = null;
                model.Dispose();
                DeckleWhispSource.Log.ModelUnloadedMilestone();
            }
        }
    }

    private static string PrecisionName(ParakeetEncoderPrecision precision) =>
        precision == ParakeetEncoderPrecision.Fp16 ? "fp16" : "fp32";

    // -1 when the size cannot be read: a log field must never fail the load it describes.
    private static double FileSizeMb(string path)
    {
        try { return new FileInfo(path).Length / 1024.0 / 1024.0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return -1; }
    }
}
