using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

// Reads the typed-text corpus back off disk: one autocorrect.text.jsonl line per
// completed sentence, the shape the JsonlSink writes —
//   { "timestamp": …, "kind": "autocorrect_text", "session": …,
//     "payload": { "process": …, "typed": …, "final": …, "history": … } }
// — into the same SentenceCorpus.SentenceRecord the accumulator emitted, so the
// offline replay reads exactly what the live engine collected. Only the payload
// matters here; the envelope (timestamp, session, process) is provenance the
// calibration pass does not need.
//
// Tolerant by design, like the ASR CorpusLoader: a blank line, a truncated tail
// line from a crash mid-write, or a line missing its payload is skipped rather
// than fatal — a corpus is an append log that may end mid-flight.
public static class CorpusReader
{
    // Streams the records lazily so a large corpus never lands in memory at once.
    public static IEnumerable<SentenceCorpus.SentenceRecord> Read(string path)
    {
        foreach (string line in File.ReadLines(path))
            if (TryParse(line, out SentenceCorpus.SentenceRecord record))
                yield return record;
    }

    // Parses one JSONL line into a record; false when the line is blank, not
    // JSON, carries no payload, or has neither a typed nor a final side.
    public static bool TryParse(string line, out SentenceCorpus.SentenceRecord record)
    {
        record = default;
        string trimmed = line.Trim();
        if (trimmed.Length == 0)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (!doc.RootElement.TryGetProperty("payload", out JsonElement payload))
                return false;

            string typed = String(payload, "typed");
            string final = String(payload, "final");
            if (typed.Length == 0 && final.Length == 0)
                return false;

            record = new SentenceCorpus.SentenceRecord(typed, final, String(payload, "history"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string String(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;
}
