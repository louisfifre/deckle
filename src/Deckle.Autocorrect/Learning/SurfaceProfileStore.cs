using System.IO;
using System.Text.Json;

namespace Deckle.Autocorrect;

// One surface's live-facing profile (CONTEXT.md § Surface profile): the ONLY
// thing the engine needs to know — where the pause pass is armed and with what
// threshold. All the statistics behind that number (closure mix, gap
// percentiles) stay in the offline artifact and its markdown report; the live
// contract is deliberately minimal so the measurement can grow without
// touching the engine. PauseThresholdMs 0 means the surface does not qualify.
public sealed record SurfaceProfileRecord(string Process, int PauseThresholdMs);

// Loads the measured surface profiles from the module's user-data root, where
// the ventilation gesture writes them — a measured artifact, never a
// user-exposed setting. Same tolerance contract as the family store: no file
// or a corrupt one is an empty set and the pause pass simply stays inert.
public static class SurfaceProfileStore
{
    public const string FileName = "surface-profiles.json";

    public static IReadOnlyList<SurfaceProfileRecord> Load(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<SurfaceProfileRecord>();

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<SurfaceProfileRecord>();

            var records = new List<SurfaceProfileRecord>();
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                string process = item.TryGetProperty("process", out JsonElement p)
                    && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
                int threshold = item.TryGetProperty("pauseThresholdMs", out JsonElement t)
                    && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
                if (process.Length == 0)
                    continue; // an unreadable record is skipped, never fatal
                records.Add(new SurfaceProfileRecord(process, Math.Max(0, threshold)));
            }
            return records;
        }
        catch (JsonException)
        {
            return Array.Empty<SurfaceProfileRecord>();
        }
    }
}
