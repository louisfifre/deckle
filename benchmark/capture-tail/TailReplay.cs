using Deckle.Audio;
using Deckle.Core;
using Deckle.Diagnostics.Telemetry;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.Benchmark.CaptureTail;

// Word-level view of the tail: replays each raw take through the production
// WhisperBackend with the tail cut to 300 and 200 ms (the recorded one is 400)
// and counts the words that change at the END of the text. Nothing but counts and
// milliseconds is printed.
//
// Variants, built from the recorded take (which ends RecordedTailMs after the press):
//   400      the file as recorded: the reference.
//   400 again  a second run of the same audio. Whisper does not repeat itself
//              exactly on the GPU, so this is the noise floor: a variant only
//              means something by what it adds over this control.
//   300, 200 the file cut by 100 and 200 ms: what a shorter tail would have heard.
//   600      NOT replayable: no audio exists past the recorded 400 ms. The
//            variant appends 200 ms of digital silence, which is all a 600 ms
//            tail adds in a take whose speech ended before the press. It shows
//            what extra silence costs, never what extra audio would gain.
//
// Metrics are taken on the last FinalWords words only, because a difference in
// the middle of a 20 s take says nothing about the tail:
//   - final words missing: of the reference's last FinalWords words, those not
//     found among the variant's last FinalWords + 2 words.
//   - last word differs: the reference's last word is not the variant's last word.
// They are printed for all takes and for the takes whose cut region holds sound
// (energy above AudibleDbfs in the cut 100 or 200 ms), the only ones where the
// variant hears something different from the reference.
// The reference is not a ground truth: a word the 400 ms tail itself cut is
// invisible here.
internal static class TailReplay
{
    private const int FinalWords = 4;
    private const double AudibleDbfs = -45;
    private const int FrameMs = 20;

    private sealed record Variant(string Label, int TailMs, int CutMs, Func<float[], float[]> Build);

    private static readonly Variant[] Variants =
    [
        new("400 again", 400, 0, s => s),
        new("300", 300, 100, s => Cut(s, 100)),
        new("200", 200, 200, s => Cut(s, 200)),
        new("600 (silence added)", 600, 0, s => Pad(s, 200)),
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

        var all = Variants.ToDictionary(v => v, _ => new Tally());
        var audible = Variants.ToDictionary(v => v, _ => new Tally());
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
            double trailingMs = TrailingMs(s);
            foreach (Variant v in Variants)
            {
                TranscriptionResult r = await Transcribe(backend, v.Build(s));
                string[] words = WordMetrics.Words(r.FullText);
                all[v].Add(refWords, words, r.TotalDurationMs, v.TailMs);
                if (v.CutMs > 0 && trailingMs < v.CutMs)
                    audible[v].Add(refWords, words, r.TotalDurationMs, v.TailMs);
            }
            if (takes % 10 == 0) Console.Error.WriteLine($"{takes} takes replayed");
        }

        Stats.Result($"replay: takes compared {takes}, skipped (empty reference text) {emptyReference}, skipped (aborted) {aborted}; final words = last {FinalWords}");
        Stats.Result($"replay: reference words total {reference.RefWords}, whisper ms p50 {Stats.Percentile(reference.WhisperMs, 50):F0}, p90 {Stats.Percentile(reference.WhisperMs, 90):F0}");
        foreach (Variant v in Variants)
        {
            Print($"tail {v.Label}, all takes", all[v], takes);
            if (v.CutMs > 0)
                Print($"tail {v.Label}, takes with sound in the cut {v.CutMs} ms", audible[v], audible[v].Takes);
        }
    }

    private static void Print(string label, Tally t, int takes)
    {
        Stats.Result($"{label}: takes {t.Takes}");
        if (t.Takes == 0) return;
        Stats.Result($"{label}: last word differs {Stats.Share(t.LastWordDiffers, takes)}; takes with a final word missing {Stats.Share(t.FinalMissingTakes, takes)}; final words missing {t.FinalMissing} of {takes * FinalWords}");
        Stats.Result($"{label}: whole-text words differ {Stats.Share(t.Differ, takes)}; word edit distance {100.0 * t.Edits / Math.Max(1, t.RefWords):F2}% of reference words; fewer words in {Stats.Share(t.FewerTakes, takes)}");
        Stats.Result($"{label}: Stop to text ms (tail + whisper) p50 {Stats.Percentile(t.StopToText, 50):F0}, p90 {Stats.Percentile(t.StopToText, 90):F0}; whisper alone p50 {Stats.Percentile(t.WhisperMs, 50):F0}");
    }

    // Milliseconds from the last audible frame to the end of the take.
    private static double TrailingMs(float[] s)
    {
        double[] db = WavSamples.FrameDb(s, FrameMs);
        int last = Array.FindLastIndex(db, d => d >= AudibleDbfs);
        return last < 0 ? double.PositiveInfinity : s.Length * 1000.0 / WavSamples.SampleRate - (last + 1) * FrameMs;
    }

    private static Task<TranscriptionResult> Transcribe(WhisperBackend backend, float[] pcm) =>
        backend.TranscribeAsync(pcm, segmentSink: null, ct: CancellationToken.None,
            context: new TranscriptionContext(PrimingText: string.Empty));

    private sealed class Tally
    {
        public int Takes, Differ, FewerTakes, Edits, RefWords, LastWordDiffers, FinalMissing, FinalMissingTakes;
        public List<double> WhisperMs { get; } = [];
        public List<double> StopToText { get; } = [];

        public void Add(string[] reference, string[] words, long whisperMs, int tailMs)
        {
            Takes++;
            RefWords += reference.Length;
            WhisperMs.Add(whisperMs);
            StopToText.Add(tailMs + whisperMs);
            int edits = WordMetrics.EditDistance(reference, words);
            Edits += edits;
            if (edits > 0) Differ++;
            if (words.Length < reference.Length) FewerTakes++;
            if (words.Length == 0 || words[^1] != reference[^1]) LastWordDiffers++;
            int missing = WordMetrics.FinalWordsMissing(reference, words, FinalWords);
            FinalMissing += missing;
            if (missing > 0) FinalMissingTakes++;
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
