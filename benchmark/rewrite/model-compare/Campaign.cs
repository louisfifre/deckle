using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Deckle.Benchmark.ModelCompare;

// ─── A campaign: the whole matrix of a run, declared in one JSON file ────────
//
// The first study ran one profile over a handful of dictations, one pass each.
// A campaign repeats every cell n times so latency, cost and the judge's
// verdicts come with their spread, and lets each profile run on the dictation
// sizes it is meant for (the app picks a profile by word count, so Arrangement
// on an 80-word dictation would measure nothing the app ever does).
//
// The file names the models, the iterations, the cells (profile × buckets) and
// the judge. The dictations are chosen at resolution time from the corpus and
// the live profile prompts are read from settings.json; both are frozen into
// results/<stamp>/campaign.json, so a resumed run and the dashboard read the
// same matrix even if the corpus grew or a prompt changed in Settings.

sealed record CampaignCell(string Profile, string[] Buckets, string? JudgeNote);

sealed record CampaignJudge(string Model, int Batch);

sealed record Campaign(
    string Name,
    string[] Models,
    int Iterations,
    int TakePerBucket,
    CampaignCell[] Cells,
    Dictionary<string, string[]>? Dictations,
    CampaignJudge Judge);

// The frozen prompt of one profile, as the run sent it. The hash is this
// harness's own (SHA-256, first 16 hex) and only says "same bytes as before".
sealed record FrozenPrompt(string Profile, string Name, string Hash, string SystemPrompt);

sealed record ResolvedCampaign(
    Campaign Campaign,
    Dictionary<string, string[]> Dictations,
    Dictionary<string, FrozenPrompt> Prompts,
    DateTimeOffset ResolvedAt);

static class CampaignFile
{
    public static Campaign Load(string path)
    {
        var campaign = JsonSerializer.Deserialize<Campaign>(File.ReadAllText(path), Json.Input)
            ?? throw new ArgumentException($"{path} holds no campaign.");
        if (campaign.Models is not { Length: > 0 }) throw new ArgumentException("A campaign names at least one model.");
        if (campaign.Iterations < 1) throw new ArgumentException("A campaign runs at least one iteration.");
        if (campaign.Cells is not { Length: > 0 }) throw new ArgumentException("A campaign has at least one cell.");
        if (campaign.Judge is null || campaign.Judge.Batch < 1) throw new ArgumentException("A campaign names its judge model and a batch size of at least 1.");
        return campaign;
    }

    // results/<stamp>/campaign.json: written once, read back on every resume.
    public static ResolvedCampaign Resolve(Campaign campaign, string outDir, Dictionary<string, RawDictation> raw)
    {
        string frozenPath = Path.Combine(outDir, "campaign.json");
        if (File.Exists(frozenPath))
        {
            var frozen = JsonSerializer.Deserialize<ResolvedCampaign>(File.ReadAllText(frozenPath), Json.Input)
                ?? throw new InvalidOperationException($"{frozenPath} is unreadable.");
            Console.WriteLine($"resuming the matrix frozen at {frozen.ResolvedAt:yyyy-MM-dd HH:mm}");
            return frozen;
        }

        var dictations = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string bucket in campaign.Cells.SelectMany(c => c.Buckets).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (campaign.Dictations is { } given && given.TryGetValue(bucket, out var ids) && ids.Length > 0)
            {
                foreach (string id in ids)
                    if (!raw.ContainsKey(id)) throw new ArgumentException($"Transcription {id} is not in the raw corpus.");
                dictations[bucket] = ids;
            }
            else
            {
                dictations[bucket] = PickTypical(raw, bucket, campaign.TakePerBucket);
            }
        }

        var prompts = new Dictionary<string, FrozenPrompt>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in campaign.Cells)
        {
            if (prompts.ContainsKey(cell.Profile)) continue;
            var variant = Corpus.LoadProfilePrompt(cell.Profile);
            prompts[cell.Profile] = new FrozenPrompt(cell.Profile, variant.Name, Hash(variant.SystemPrompt), variant.SystemPrompt);
        }

        var resolved = new ResolvedCampaign(campaign, dictations, prompts, DateTimeOffset.Now);
        Directory.CreateDirectory(outDir);
        File.WriteAllText(frozenPath, JsonSerializer.Serialize(resolved, Json.Pretty));
        return resolved;
    }

    // The n dictations closest to the bucket's median word count, so a
    // "short" cell measures a typical short dictation rather than the
    // three-word one that happens to be the most recent. Ties go to the most
    // recent.
    static string[] PickTypical(Dictionary<string, RawDictation> raw, string bucket, int take)
    {
        var inBucket = raw.Values
            .Where(r => string.Equals(r.Bucket, bucket, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.WordsIn)
            .ToList();
        if (inBucket.Count == 0) throw new ArgumentException($"The raw corpus has no dictation in bucket '{bucket}'.");
        int median = inBucket[inBucket.Count / 2].WordsIn;
        return inBucket
            .OrderBy(r => Math.Abs(r.WordsIn - median))
            .ThenByDescending(r => r.Timestamp)
            .Take(take)
            .Select(r => r.Id)
            .ToArray();
    }

    static string Hash(string text)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
    }
}
