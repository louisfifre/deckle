using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

// One corpus line, parsed: the record the accumulator emitted, plus whether the
// « history » property was present on the wire. Absence is not emptiness — a line
// that predates the history field (pre-2026-07-02) carries NO property, and the
// alignment must treat it differently from a modern record that simply had nothing
// to correct (property present, value ""). Only the reader can tell the two apart,
// so it surfaces the fact rather than folding it into an empty string.
public readonly record struct CorpusEntry(SentenceCorpus.SentenceRecord Record, bool HistoryPresent);

// Reads the typed-text corpus back off disk: one autocorrect.text.jsonl line per
// completed sentence, the shape the JsonlSink writes —
//   { "timestamp": …, "kind": "autocorrect_text", "session": …,
//     "payload": { "process": …, "typed": …, "final": …, "history": …,
//                  "closure": …, "timing": … } }
// — into the same SentenceCorpus.SentenceRecord the accumulator emitted, so the
// offline replay reads exactly what the live engine collected. Only the payload
// matters here; the envelope (timestamp, session, process) is provenance the
// calibration pass does not need.
//
// Tolerant by design, like the ASR CorpusLoader: a blank line, a truncated tail
// line from a crash mid-write, or a line missing its payload is skipped rather
// than fatal — a corpus is an append log that may end mid-flight. Legacy lines are
// tolerated too: a line may lack « history » (pre-2026-07-02) and will lack
// « closure »/« timing »; closure defaults to "sentence", timing to "".
public static class CorpusReader
{
    // Streams the records lazily so a large corpus never lands in memory at once.
    public static IEnumerable<CorpusEntry> Read(string path)
    {
        foreach (string line in File.ReadLines(path))
            if (TryParse(line, out CorpusEntry entry))
                yield return entry;
    }

    // Parses one JSONL line into an entry; false when the line is blank, not JSON,
    // carries no payload, or has neither a typed nor a final side. HistoryPresent
    // reflects whether the « history » property existed on the line, not whether it
    // held anything.
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

            // Closure names how the run ended (a legitimate context regardless);
            // a missing or blank value means a legacy record, defaulted to a normal
            // sentence-ending close. Timing is unused by the replay but kept whole.
            string closure = String(payload, "closure");
            if (closure.Length == 0)
                closure = "sentence";

            var record = new SentenceCorpus.SentenceRecord(
                typed, final, String(payload, "history"), closure, String(payload, "timing"));
            entry = new CorpusEntry(record, historyPresent);
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
