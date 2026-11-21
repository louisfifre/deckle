using Deckle.Audio;
using Deckle.Core;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.Benchmark.CaptureTail;

// Word-level view of the tail: replays each raw take through the production
// WhisperBackend with the tail cut to 200, 300 and 400 ms (the recorded one) and
// counts the words that change. Nothing but counts and milliseconds is printed.
//
// Variants, built from the recorded take (which ends RecordedTailMs after the press):
//   400      the file as recorded: the reference.
//   400 again  a second run of the same audio: the noise floor of the comparison
//              (whisper is not guaranteed to repeat itself on the GPU).
//   300, 200 the file cut by 100 and 200 ms: what a shorter tail would have heard.
//   600      NOT replayable: no audio exists past the recorded 400 ms. The
//            variant appends 200 ms of digital silence, which is what a 600 ms
//            tail would add in a take whose speech ended before the press and
//            nothing more. It bounds the cost (words changed by silence alone),
//            never the gain.
// The reference is not a ground truth: a word the 400 ms tail itself cut is
// invisible here. That is why the share of takes with sound up to the end of the
// file is printed next to it.
internal static class TailReplay
{
    private sealed record Variant(string Label, int TailMs, Func<float[], float[]> Build);

    private static readonly Variant[] Variants =
    [
        new("400 again", 400, s => s),
        new("300", 300, s => Cut(s, 100)),
        new("200", 200, s => Cut(s, 200)),
        new("600 (silence added)", 600, s => Pad(s, 200)),
    ];

    private static float[] Cut(float[] s, int ms) =>
        s[..Math.Max(0, s.Length - ms * WavSamples.SampleRate / 1000)];

    private static float[] Pad(float[] s, int ms)
    {
        var r = new float[s.Length + ms * WavSamples.SampleRate / 1000];
        s.CopyTo(r, 0);
        return r;
    }

    public static async Task RunAsync(IReadOnlyList<string> wavPaths, int maxTakes)
    {
        if (!NativeRuntime.IsInstalled() || !NativeRuntime.CheckLoad().Loads)
        {
            Stats.Result("replay: the installed whisper native runtime is missing or does not load");
            return;
        }

        var host = new ReplayHost();
        using var backend = new WhisperBackend(host);
        ModelLoadResult loaded = await backend.LoadModelAsync(CancellationToken.None);
        if (!loaded.Success)
        {
            Stats.Result($"replay: model load failed ({loaded.ErrorReason})");
            return;
        }
        Stats.Result($"replay: model {host.Transcription.Engine.Model}, accelerator {backend.DetectedAccelerator}, language {host.Transcription.Engine.Language}, shipped defaults otherwise");

        var perVariant = Variants.ToDictionary(v => v, _ => new Tally());
        var reference = new Tally();
        int takes = 0, emptyReference = 0, aborted = 0;

        foreach (string path in wavPaths.Take(maxTakes))
        {
            float[]? s = WavSamples.Read(path);
            if (s is null || s.Length < WavSamples.SampleRate) continue;

            TranscriptionResult baseline = await Transcribe(backend, s);
            if (baseline.Aborted) { aborted++; continue; }
            string[] refWords = WordMetrics.Words(baseline.FullText);
            if (refWords.Length == 0) { emptyReference++; continue; }

            takes++;
            reference.Add(refWords, refWords, baseline.TotalDurationMs, 400);
            foreach (Variant v in Variants)
            {
                TranscriptionResult r = await Transcribe(backend, v.Build(s));
                string[] words = WordMetrics.Words(r.FullText);
                perVariant[v].Add(refWords, words, r.TotalDurationMs, v.TailMs);
            }
            if (takes % 20 == 0) Console.Error.WriteLine($"{takes} takes replayed");
        }

        Stats.Result($"replay: takes compared {takes}, skipped (empty reference text) {emptyReference}, skipped (aborted) {aborted}");
        Stats.Result($"replay: reference words total {reference.RefWords}, whisper ms p50 {Stats.Percentile(reference.WhisperMs, 50):F0}, p90 {Stats.Percentile(reference.WhisperMs, 90):F0}");
        foreach (Variant v in Variants)
        {
            Tally t = perVariant[v];
            Stats.Result($"tail {v.Label}: takes whose words differ from the 400 ms reference {Stats.Share(t.Differ, takes)}");
            Stats.Result($"tail {v.Label}: takes with final words lost or changed {Stats.Share(t.FinalAffectedTakes, takes)}; final words affected {t.FinalAffectedWords} of {t.RefWords} ({100.0 * t.FinalAffectedWords / Math.Max(1, t.RefWords):F2}%)");
            Stats.Result($"tail {v.Label}: takes with fewer words {Stats.Share(t.FewerTakes, takes)}; words lost net {t.WordsLost}; word edit distance {t.Edits} ({100.0 * t.Edits / Math.Max(1, t.RefWords):F2}% of reference words)");
            Stats.Result($"tail {v.Label}: Stop to text ms (tail + whisper) p50 {Stats.Percentile(t.StopToText, 50):F0}, p90 {Stats.Percentile(t.StopToText, 90):F0}; whisper alone p50 {Stats.Percentile(t.WhisperMs, 50):F0}");
        }
    }

    private static Task<TranscriptionResult> Transcribe(WhisperBackend backend, float[] pcm) =>
        backend.TranscribeAsync(pcm, segmentSink: null, ct: CancellationToken.None,
            context: new TranscriptionContext(PrimingText: string.Empty));

    private sealed class Tally
    {
        public int Differ, FinalAffectedTakes, FinalAffectedWords, FewerTakes, WordsLost, Edits, RefWords;
        public List<double> WhisperMs { get; } = [];
        public List<double> StopToText { get; } = [];

        public void Add(string[] reference, string[] words, long whisperMs, int tailMs)
        {
            RefWords += reference.Length;
            WhisperMs.Add(whisperMs);
            StopToText.Add(tailMs + whisperMs);
            int edits = WordMetrics.EditDistance(reference, words);
            Edits += edits;
            if (edits > 0) Differ++;
            int affected = WordMetrics.FinalWordsAffected(reference, words);
            if (affected > 0) { FinalAffectedTakes++; FinalAffectedWords += affected; }
            if (words.Length < reference.Length) { FewerTakes++; WordsLost += reference.Length - words.Length; }
        }
    }

    private sealed class ReplayHost : ITranscriptionEngineHost
    {
        public ReplayHost()
        {
            Transcription.Engine.Model = "ggml-large-v3.bin";
            Llm.Enabled = false;
        }

        public TranscriptionSettings Transcription { get; } = new();
        public CaptureSettings Audio { get; } = new();
        public TelemetrySettings Telemetry { get; } = new();
        public LlmSettings Llm { get; } = new();
        public string ResolveModelsDirectory() => AppPaths.ModelsDirectory;
    }
}
