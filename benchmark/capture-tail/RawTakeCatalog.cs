using System.Text.Json;

namespace Deckle.Benchmark.CaptureTail;

// Which corpus WAVs are raw captures, from the corpus metadata and not from a
// guess on the audio. Each ASR line of the corpus carries `audio_file` and
// `audio_content` ("raw" or "processed", the latter being the VAD-trimmed
// buffer). This reader parses each line in memory, keeps only those two
// strings, and drops the line: nothing else of a line (it holds the dictated
// text) is stored, printed or logged. Only counts leave this class.
internal sealed class RawTakeCatalog
{
    public HashSet<string> RawFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Processed { get; private set; }
    public int Unlabeled { get; private set; }

    public static RawTakeCatalog Load(string corpusRoot)
    {
        var catalog = new RawTakeCatalog();
        string rawBucket = Path.Combine(corpusRoot, "raw");
        if (!Directory.Exists(rawBucket)) return catalog;

        foreach (string jsonl in Directory.EnumerateFiles(rawBucket, "corpus.jsonl", SearchOption.AllDirectories))
        {
            // The running app appends to this file: share read and write.
            using var stream = new FileStream(jsonl, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
                catalog.Add(line);
        }
        return catalog;
    }

    private void Add(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;
            JsonElement scope = root.TryGetProperty("payload", out JsonElement p) ? p : root;
            string file = Str(scope, "audio_file");
            string content = Str(scope, "audio_content");
            if (file.Length == 0) return;
            if (content == "raw") RawFiles.Add(file);
            else if (content == "processed") Processed++;
            else Unlabeled++;
        }
        catch (JsonException)
        {
            // A crash can leave a truncated last line: skipped, as the other readers do.
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
}
