using System.Diagnostics;
using Deckle.App.SelfCheck;
using Deckle.Core;
using Deckle.Transcription;
using Deckle.Transcription.Parakeet;
using Deckle.Transcription.Whisper;

namespace Deckle.App;

// ── Self-check, measurement side ─────────────────────────────────────────────
//
// The bench (scripts/bench/perf-bench.ps1) runs the self-check and reads its
// report and its log. Two things were not in them: the process figures
// (SelfCheckPerf), taken here, and a dictation take, which the self-check leaves
// out on purpose (the engine loads the GPU, see LeftOutOfSelfCheck). The take is
// opt-in through an environment variable the bench sets, so a plain self-check
// never composes the engine.
public partial class App
{
    // The path of the audio file to transcribe. Absent: no take.
    internal const string BenchDictationEnvVar = "DECKLE_BENCH_DICTATION";

    // The engine composed as the resident boot composes it (App.Startup.Modules.cs):
    // same gates, same backend choice, on the run's own data root. The file takes
    // the road of a tray selection; its phases are logged as PipelineTimings.
    private static async Task<SelfCheckDictation?> MeasureDictationForBenchAsync()
    {
        string? audioPath = Environment.GetEnvironmentVariable(BenchDictationEnvVar);
        if (string.IsNullOrWhiteSpace(audioPath)) return null;

        if (!File.Exists(audioPath))
            return new(SelfCheckDictation.Failed, "audio_file_missing", null, 0, 0);

        bool useParakeet = TranscriptionSettingsService.Instance.Current.Engine.Backend == AsrBackendKind.Parakeet;
        string backendName = useParakeet ? "parakeet" : "whisper";
        string? skip =
            useParakeet ? (ParakeetModelPresent() ? null : SkipSpeechModelMissing)
            : !NativeRuntime.IsInstalled() ? SkipNativeRuntimeMissing
            : !NativeRuntime.CheckLoad().Loads ? SkipNativeRuntimeUnloadable
            : !SpeechModels.IsAnyModelInstalled() ? SkipSpeechModelMissing
            : null;
        if (skip is not null)
            return new(SelfCheckDictation.Skipped, skip, backendName, 0, 0);

        var host = new AppTranscriptionEngineHost();
        IAsrBackend backend = useParakeet ? new ParakeetBackend(host) : new WhisperBackend(host);
        var engine = new TranscriptionEngine(host, backend);
        try
        {
            // The tray's road, not the window's: only it closes on the shared
            // finalize, which logs the take's PipelineTimings. It writes the
            // transcript beside the audio, in the bench's own folder.
            var finished = new TaskCompletionSource<TranscriptionOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.TranscriptionFinished += outcome => finished.TrySetResult(outcome);

            var clock = Stopwatch.StartNew();
            if (engine.EnqueueFileTranscriptions([audioPath]) == 0)
                return new(SelfCheckDictation.Failed, "not_queued", backendName, 0, 0);
            Task done = await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromMinutes(5)));
            clock.Stop();
            if (done != finished.Task)
                return new(SelfCheckDictation.Failed, "timeout", backendName, clock.ElapsedMilliseconds, 0);

            string transcript = Path.ChangeExtension(audioPath, ".txt");
            int chars = File.Exists(transcript) ? (int)new FileInfo(transcript).Length : 0;
            return finished.Task.Result == TranscriptionOutcome.SavedToFile
                ? new(SelfCheckDictation.Ok, null, backendName, clock.ElapsedMilliseconds, chars)
                : new(SelfCheckDictation.Failed, "no_text", backendName, clock.ElapsedMilliseconds, 0);
        }
        finally
        {
            engine.Dispose();
        }
    }
}
