using System.Text.Json;
using Deckle.Benchmark.PhiBench.Models;

namespace Deckle.Benchmark.PhiBench;

/// <summary>
/// Reads a Deckle telemetry corpus from a folder containing
/// <c>corpus.jsonl</c> + sibling <c>.wav</c> files. Mirrors the loader in
/// benchmark/lib/corpus.py — same JSONL format, same Sample fields, sorted
/// by ascending duration so the bench warms up on short samples first.
///
/// Silently filters samples whose audio file is missing — a corpus can be
/// partial (Louis may have deleted a wav to test something), the bench should
/// not crash.
/// </summary>
public static class CorpusLoader
{
    public static List<Sample> Load(string corpusDir)
    {
        var jsonlPath = Path.Combine(corpusDir, "corpus.jsonl");
        if (!File.Exists(jsonlPath))
            throw new FileNotFoundException($"corpus.jsonl not found in {corpusDir}", jsonlPath);

        var samples = new List<Sample>();
        foreach (var line in File.ReadLines(jsonlPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            using var doc = JsonDocument.Parse(trimmed);
            if (!doc.RootElement.TryGetProperty("payload", out var payload)) continue;

            var audioFile = payload.GetProperty("audio_file").GetString() ?? string.Empty;
            var wavPath = Path.Combine(corpusDir, audioFile);
            if (!File.Exists(wavPath)) continue;

            string GetString(string key) => payload.TryGetProperty(key, out var v) ? v.GetString() ?? string.Empty : string.Empty;
            double GetDouble(string key) => payload.TryGetProperty(key, out var v) ? v.GetDouble() : 0.0;
            int GetInt(string key) => payload.TryGetProperty(key, out var v) ? v.GetInt32() : 0;

            samples.Add(new Sample(
                Id: GetString("transcription_id"),
                AudioFile: audioFile,
                AudioPath: wavPath,
                DurationSeconds: GetDouble("duration_seconds"),
                Tier: GetString("tier"),
                ReferenceTextWhisper: GetString("text"),
                ReferenceWordsWhisper: GetInt("text_words"),
                ReferenceTextGemini: GetString("reference_text_gemini")));
        }

        samples.Sort((a, b) => a.DurationSeconds.CompareTo(b.DurationSeconds));
        return samples;
    }
}
