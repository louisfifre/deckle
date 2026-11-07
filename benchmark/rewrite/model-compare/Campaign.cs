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

// A cell runs one prompt on some buckets. By default the prompt is the live
// one of `Profile`, read from settings.json; `PromptFile` runs a candidate
// wording instead, under the same profile (so the export still says which
// production profile it stands for) and with its own judge note, since a
// candidate may allow what the live prompt forbids. `Key` is what the rows
// record as their prompt: the profile's name, or the file's, or `Name`.
sealed record CampaignCell(string Profile, string[] Buckets, string? JudgeNote, string? Name = null, string? PromptFile = null)
{
    public string Key => Name ?? (PromptFile is null ? Profile : Path.GetFileNameWithoutExtension(PromptFile));
}

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
        var duplicate = campaign.Cells.GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new ArgumentException($"Two cells share the prompt key '{duplicate.Key}': give one of them a name.");
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
            if (prompts.ContainsKey(cell.Key)) continue;
            // A file candidate is named by the cell's key, a live profile by
            // Corpus's "profile:<name>", so a row's prompt says which it was.
            var variant = cell.PromptFile is { } file
                ? Corpus.LoadPromptFile(file) with { Name = cell.Key }
                : Corpus.LoadProfilePrompt(cell.Profile);
            prompts[cell.Key] = new FrozenPrompt(cell.Profile, variant.Name, Hash(variant.SystemPrompt), variant.SystemPrompt);
        }

        var resolved = new ResolvedCampaign(campaign, dictations, prompts, DateTimeOffset.Now);
        Directory.CreateDirectory(outDir);
        File.WriteAllText(frozenPath, JsonSerializer.Serialize(resolved, Json.Pretty));
        return resolved;
    }

    // A matrix resolved in code (the optimizer builds one per round) is frozen
    // the same way: the file wins when it exists, so a resumed round reads the
    // prompt it was started with even if the candidate file was edited since.
    public static ResolvedCampaign Freeze(ResolvedCampaign candidate, string outDir)
    {
        string frozenPath = Path.Combine(outDir, "campaign.json");
        if (File.Exists(frozenPath))
            return JsonSerializer.Deserialize<ResolvedCampaign>(File.ReadAllText(frozenPath), Json.Input)
                ?? throw new InvalidOperationException($"{frozenPath} is unreadable.");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(frozenPath, JsonSerializer.Serialize(candidate, Json.Pretty));
        return candidate;
    }

    // The n dictations closest to the bucket's median word count, so a
    // "short" cell measures a typical short dictation rather than the
    // three-word one that happens to be the most recent. Ties go to the most
    // recent. `exclude` lets a held-out set take the next typical ones after
    // a training set, never the same dictations.
    public static string[] PickTypical(Dictionary<string, RawDictation> raw, string bucket, int take, ISet<string>? exclude = null)
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
            .Where(r => exclude is null || !exclude.Contains(r.Id))
            .Take(take)
            .Select(r => r.Id)
            .ToArray();
    }

    public static string Hash(string text)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
    }
}
