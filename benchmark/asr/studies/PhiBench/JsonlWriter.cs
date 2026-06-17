using System.Text.Encodings.Web;
using System.Text.Json;
using Deckle.Benchmark.PhiBench.Models;

namespace Deckle.Benchmark.PhiBench;

/// <summary>
/// Writes bench rows in the JSONL schema produced by the Python bench's
/// <c>_build_row()</c> in benchmark/asr/studies/voxtral-validation/bench.py.
/// Field names and types match exactly so the Python post-processors
/// (lib.metrics.*, lib.judges.gemini) can consume the rows unchanged.
///
/// One row per (sample, regime). <c>metrics</c> and <c>judge</c> are left
/// null here — they are added downstream by the Python enrichment pass.
/// </summary>
public sealed class JsonlWriter : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // preserve accents as-is
        WriteIndented = false,
    };

    private readonly StreamWriter _writer;

    public JsonlWriter(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(path, append: false, System.Text.Encoding.UTF8) { AutoFlush = true };
    }

    public void WriteRow(Sample sample, Regime regime, string sourceName, string sourceLabel, TranscriptionResult result)
    {
        var row = new Dictionary<string, object?>
        {
            ["audio_id"]                = sample.Id,
            ["audio_file"]              = sample.AudioFile,
            ["audio_seconds"]           = sample.DurationSeconds,
            ["tier"]                    = sample.Tier,
            ["reference_text_whisper"]  = sample.ReferenceTextWhisper,
            ["reference_text_gemini"]   = sample.ReferenceTextGemini,
            ["reference_words_whisper"] = sample.ReferenceWordsWhisper,
            ["source"]                  = sourceName,
            ["source_label"]            = sourceLabel,
            ["regime"]                  = regime.Name,
            ["regime_label"]            = regime.Label,
            ["regime_user_prompt"]      = regime.Prompt,
            ["regime_system_prompt"]    = regime.SystemPrompt,
            ["ok"]                      = result.Ok,
            ["error"]                   = result.Error,
            ["text"]                    = result.Text,
            ["elapsed_s"]               = result.ElapsedSeconds,
            ["rtf"]                     = result.Rtf,
            ["generated_tokens"]        = result.GeneratedTokens,
            ["extras"]                  = result.Extras,
            ["timestamp"]               = DateTime.Now.ToString("s"),
            // Placeholders for Python enrichment downstream.
            ["metrics"]                 = null,
            ["judge"]                   = null,
        };
        _writer.WriteLine(JsonSerializer.Serialize(row, JsonOptions));
    }

    public void Dispose() => _writer.Dispose();
}
