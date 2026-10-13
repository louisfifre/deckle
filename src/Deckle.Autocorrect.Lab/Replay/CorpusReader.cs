using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

// HistoryPresent: absence is not emptiness. A line older than the history field
// carries NO property, and the alignment must treat it differently from a record
// that had nothing to correct (property present, value ""). Only the reader can
// tell the two apart. Day is the envelope timestamp's date (yyyy-MM-dd, "" when
// absent).
public readonly record struct CorpusEntry(
    SentenceCorpus.SentenceRecord Record, bool HistoryPresent, string Process = "", string Day = "");

// Reads the typed-text corpus back off disk: one autocorrect.text.jsonl line per
// completed sentence, the shape the JsonlSink writes —
//   { "timestamp": …, "kind": "autocorrect_text", "session": …,
//     "payload": { "process": …, "typed": …, "final": …, "history": …,
//                  "closure": …, "timing": … } }
// — into the same SentenceCorpus.SentenceRecord the accumulator emitted, so the
// offline replay reads exactly what the live engine collected.
//
// Tolerant by design: a blank line, a truncated tail line from a crash mid-write,
// or a line missing its payload is skipped rather than fatal; a corpus is an
// append log that may end mid-flight. Older lines may lack « history »,
// « closure » and « timing »; closure defaults to "sentence", timing to "".
public static class CorpusReader
{
    public static IEnumerable<CorpusEntry> Read(string path)
    {
        foreach (string line in File.ReadLines(path))
            if (TryParse(line, out CorpusEntry entry))
                yield return entry;
    }

    public static bool TryParse(string line, out CorpusEntry entry)
    {
        entry = default;
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

            bool historyPresent = payload.TryGetProperty("history", out _);

            string closure = String(payload, "closure");
            if (closure.Length == 0)
                closure = "sentence";

            // ISO 8601 on the wire, so the first ten chars are the day whatever
            // the offset.
            string stamp = String(doc.RootElement, "timestamp");
            string day = stamp.Length >= 10 ? stamp[..10] : string.Empty;

            var record = new SentenceCorpus.SentenceRecord(
                typed, final, String(payload, "history"), closure, String(payload, "timing"));
            entry = new CorpusEntry(record, historyPresent, String(payload, "process"), day);
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
