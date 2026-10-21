using System.IO;
using System.Text.Json;

namespace Deckle.Autocorrect;

// One surface's measured profile: the process and the pause threshold measured
// for it. All the statistics behind that number (closure mix, gap percentiles)
// stay in the offline artifact and its markdown report. PauseThresholdMs 0
// means the surface does not qualify.
public sealed record SurfaceProfileRecord(string Process, int PauseThresholdMs);

// Loads the measured surface profiles from the module's user-data root, where
// the surface-profiling maintenance gesture writes them — a measured artifact,
// never a user-exposed setting. Same tolerance contract as the family store: no
// file or a corrupt one is an empty set.
public static class SurfaceProfileStore
{
    public const string FileName = "surface-profiles.json";

    private const string StoreName = "surface_profiles";

    public static IReadOnlyList<SurfaceProfileRecord> Load(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<SurfaceProfileRecord>();

        // The tolerance keeps the boot alive; the events keep it from being
        // silent, because an empty set here quietly disarms the pause pass.
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                DeckleAutocorrectSource.Log.LearningStoreUnreadable(
                    StoreName, DeckleAutocorrectSource.LearningStoreReasons.NotAnArray,
                    string.Empty, string.Empty);
                return Array.Empty<SurfaceProfileRecord>();
            }

            var records = new List<SurfaceProfileRecord>();
            int skipped = 0;
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                // A non-object entry would make TryGetProperty throw and lose
                // every valid record with it.
                if (item.ValueKind != JsonValueKind.Object)
                {
                    skipped++;
                    continue;
                }
                string process = item.TryGetProperty("process", out JsonElement p)
                    && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
                // TryGetInt32: GetInt32 throws on 1.5 or 1e12, and that
                // exception escaped the JsonException catch below.
                int threshold = item.TryGetProperty("pauseThresholdMs", out JsonElement t)
                    && t.ValueKind == JsonValueKind.Number
                    && t.TryGetInt32(out int parsed) ? parsed : 0;
                if (process.Length == 0)
                {
                    skipped++;
                    continue; // an unreadable record is skipped, never fatal
                }
                records.Add(new SurfaceProfileRecord(process, Math.Max(0, threshold)));
            }
            DeckleAutocorrectSource.Log.LearningStoreLoaded(StoreName, records.Count, skipped);
            return records;
        }
        catch (JsonException ex)
        {
            // The parser's message quotes the offending token, which is user
            // data; the position locates the fault without it.
            DeckleAutocorrectSource.Log.LearningStoreUnreadable(
                StoreName, DeckleAutocorrectSource.LearningStoreReasons.MalformedJson,
                ex.GetType().FullName ?? ex.GetType().Name,
                $"line={ex.LineNumber} byte={ex.BytePositionInLine}");
            return Array.Empty<SurfaceProfileRecord>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleAutocorrectSource.Log.LearningStoreUnreadable(
                StoreName, DeckleAutocorrectSource.LearningStoreReasons.ReadFailed,
                ex.GetType().FullName ?? ex.GetType().Name, ex.Message);
            return Array.Empty<SurfaceProfileRecord>();
        }
    }
}
