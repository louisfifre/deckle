using System.Text.Json;
using Deckle.Core;

namespace Deckle.Benchmark.ModelCompare;

// ─── Inputs: the telemetry corpus and the prompt variants ────────────────────
//
// The corpus is runtime data: dictations are read from the user data root at
// run time and only ever written to the git-ignored results folder. Nothing
// here copies a dictation into the repository.

sealed record RawDictation(string Id, string Bucket, DateTimeOffset Timestamp, int WordsIn, string Text);

sealed record PromptVariant(string Name, string SystemPrompt);

sealed record ProfileUnderTest(PromptVariant Prompt, string Model, Deckle.Llm.ReasoningLevel? Reasoning);

static class Corpus
{
    // AppPaths resolves the data root the way the app does (DECKLE_DATA_ROOT
    // first), so a dev data folder is read when one is configured.
    static string Root => Path.Combine(AppPaths.TelemetryDirectory, "corpus");

    // Every raw dictation, keyed by transcription id. The bucket is the folder
    // name (very-short .. very-long): the payload's own "bucket" field says
    // "raw" and its "tier" carries the size class. A transcription written
    // twice keeps its last line.
    public static Dictionary<string, RawDictation> LoadRaw()
    {
        var byId = new Dictionary<string, RawDictation>(StringComparer.OrdinalIgnoreCase);
        string rawRoot = Path.Combine(Root, "raw");
        if (!Directory.Exists(rawRoot))
            throw new DirectoryNotFoundException($"No raw corpus at {rawRoot}.");

        foreach (string bucketDir in Directory.EnumerateDirectories(rawRoot))
        {
            string file = Path.Combine(bucketDir, "corpus.jsonl");
            if (!File.Exists(file)) continue;
            string bucket = Path.GetFileName(bucketDir);
            foreach (var (timestamp, payload) in ReadPayloads(file))
            {
                string? id = Str(payload, "transcription_id");
                string? text = Str(payload, "text");
                if (id is null || string.IsNullOrWhiteSpace(text)) continue;
                int words = payload.TryGetProperty("text_words", out var w) && w.TryGetInt32(out int n) ? n : Words.Count(text);
                byId[id] = new RawDictation(id, bucket, timestamp, words, text);
            }
        }
        return byId;
    }

    // --ids wins over --bucket/--take; an unknown id is an error, not a
    // silent skip, because a typo would otherwise shrink the matrix unnoticed.
    public static List<RawDictation> Select(Dictionary<string, RawDictation> raw, string? ids, string? bucket, int take)
    {
        if (!string.IsNullOrWhiteSpace(ids))
        {
            var selected = new List<RawDictation>();
            foreach (string id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!raw.TryGetValue(id, out var dictation))
                    throw new ArgumentException($"Transcription {id} is not in the raw corpus.");
                selected.Add(dictation);
            }
            return selected;
        }

        if (string.IsNullOrWhiteSpace(bucket))
            throw new ArgumentException("Select dictations with --ids a,b,c or --bucket <name> --take N.");

        return raw.Values
            .Where(r => string.Equals(r.Bucket, bucket, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Timestamp)
            .Take(take)
            .ToList();
    }

    // The production rewrites Deckle already recorded for these ids, from
    // every rewrite-<slug>-<profileId> bucket. They join the judge matrix as
    // source "deckle" without a new API call. The prompt is named after the
    // profile and its template hash, because a profile's prompt can change
    // under the same id: the hash says which text produced the rewrite.
    public static List<GenerationRow> LoadProductionRewrites(IReadOnlyCollection<RawDictation> selected)
    {
        var wanted = selected.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        var rows = new List<GenerationRow>();
        if (!Directory.Exists(Root)) return rows;

        foreach (string bucketDir in Directory.EnumerateDirectories(Root, "rewrite-*"))
        {
            string file = Path.Combine(bucketDir, "corpus.jsonl");
            if (!File.Exists(file)) continue;
            foreach (var (_, payload) in ReadPayloads(file))
            {
                string? id = Str(payload, "transcription_id");
                if (id is null || !wanted.TryGetValue(id, out var raw)) continue;
                string text = Str(payload, "text") ?? "";
                string profile = (Str(payload, "rewrite_profile_name") ?? "unknown").ToLowerInvariant().Replace(' ', '-');
                string hash = Str(payload, "prompt_template_hash") ?? "";
                long? elapsed = payload.TryGetProperty("elapsed_ms", out var e) && e.TryGetInt64(out long ms) ? ms : null;
                rows.Add(new GenerationRow(
                    Id: raw.Id, Bucket: raw.Bucket, WordsIn: raw.WordsIn, WordsOut: Words.Count(text),
                    Model: Str(payload, "ollama_model") ?? "unknown",
                    Prompt: hash.Length > 0 ? $"profile:{profile}@{hash}" : $"profile:{profile}",
                    Source: "deckle", Text: text,
                    PromptTokens: null, EvalTokens: null, TotalMs: elapsed, CostUsd: null, Error: null));
            }
        }
        return rows;
    }

    // A prompt file is named by its file name without extension. Line endings
    // are normalised to LF: the repository stores the snapshots with LF, and
    // a CRLF checkout must not make two runs of the same file differ.
    public static PromptVariant LoadPromptFile(string path)
    {
        string text = File.ReadAllText(path).Replace("\r\n", "\n").Trim();
        return new PromptVariant(Path.GetFileNameWithoutExtension(path), text);
    }

    // The live system prompt of a Deckle profile, byte for byte (the Settings
    // text box separates paragraphs with bare CRs; they are kept, since this
    // variant exists to reproduce exactly what the app sends).
    public static PromptVariant LoadProfilePrompt(string profileName)
    {
        string settingsPath = Path.Combine(AppPaths.ModulesDirectory, "llm", "settings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
        foreach (var profile in doc.RootElement.GetProperty("profiles").EnumerateArray())
        {
            if (string.Equals(Str(profile, "name"), profileName, StringComparison.OrdinalIgnoreCase))
                return new PromptVariant($"profile:{profileName.ToLowerInvariant()}", Str(profile, "systemPrompt") ?? "");
        }
        throw new ArgumentException($"No profile named {profileName} in {settingsPath}.");
    }

    // A profile as the app would send it: its live prompt, the model it names
    // and its reasoning level (stored as a number; absent keeps the engine's
    // default).
    public static ProfileUnderTest LoadProfile(string profileName)
    {
        string settingsPath = Path.Combine(AppPaths.ModulesDirectory, "llm", "settings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
        foreach (var profile in doc.RootElement.GetProperty("profiles").EnumerateArray())
        {
            if (!string.Equals(Str(profile, "name"), profileName, StringComparison.OrdinalIgnoreCase)) continue;
            Deckle.Llm.ReasoningLevel? reasoning = profile.TryGetProperty("reasoning", out var r) switch
            {
                true when r.ValueKind == JsonValueKind.Number => (Deckle.Llm.ReasoningLevel)r.GetInt32(),
                true when r.ValueKind == JsonValueKind.String => ModelVariant.ParseLevel(r.GetString()!),
                _ => null,
            };
            return new ProfileUnderTest(LoadProfilePrompt(profileName), Str(profile, "model") ?? "", reasoning);
        }
        throw new ArgumentException($"No profile named {profileName} in {settingsPath}.");
    }

    static IEnumerable<(DateTimeOffset Timestamp, JsonElement Payload)> ReadPayloads(string file)
    {
        // The running app keeps its corpus files open for writing, so the
        // read must share write access; File.ReadLines would be refused.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonElement root;
            try { root = JsonDocument.Parse(line).RootElement; }
            catch (JsonException) { continue; } // a torn last line from a crash
            if (!root.TryGetProperty("payload", out var payload)) continue;
            var timestamp = root.TryGetProperty("timestamp", out var t) && t.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(t.GetString(), out var parsed) ? parsed : DateTimeOffset.MinValue;
            yield return (timestamp, payload);
        }
    }

    static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
