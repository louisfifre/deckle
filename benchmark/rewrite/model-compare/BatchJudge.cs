using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace Deckle.Benchmark.ModelCompare;

// ─── The batch judge: one call reads the raw once and several rewrites ───────
//
// A campaign judges every iteration, and sending the raw dictation with each
// rewrite would bill it twenty times per cell. One call carries the raw and a
// batch of rewrites of the same cell (same dictation, profile and model, on
// separate runs), and answers one judgement per rewrite. The rewrites are
// alike by construction, which is why they are judged against the raw one by
// one and never against each other: the prompt says so, and the schema keeps
// them apart by number.
//
// The rules and the verdict scale are Judge's own constants, so a verdict
// means the same thing in both commands. The profile's note tells the judge
// what the profile is allowed to do with order: Arrangement regroups by
// theme on purpose, and a move must not read as an omission plus an addition.
//
// Same stance as Judge: effort high, no refusal fallback, a lead not a truth.

sealed record BatchJudgementRow(
    string Key,
    string Id,
    string Model,
    string Prompt,
    int[] Iterations,
    string JudgeModel,
    List<Judgement?>? Judgements,
    string? JudgeRaw,
    int? JudgePromptTokens,
    int? JudgeEvalTokens,
    long? JudgeMs,
    double? JudgeCostUsd,
    string? Error,
    int? JudgeCacheWriteTokens = null,
    int? JudgeCacheReadTokens = null);

static class BatchJudge
{
    // Five judgements with their spans, after adaptive thinking at high
    // effort on up to five long rewrites: 24 K leaves room and stays inside
    // the SDK's non-streaming timeout.
    const int MaxOutputTokens = 24_000;

    const string BatchIntro = """
        You audit the fidelity of automatic rewrites of French dictated speech.

        The text inside <raw> is a speech-recognition transcript of one person speaking French. It carries everything speech carries: hesitations, fillers, false starts, fragments repeated by the flow of speech, dropped "ne", run-on sentences, and recognition errors. Inside <rewrites> are several rewrites of that same raw, each in its own <rewrite n="..."> element. They were produced by the same system on separate runs, so they are expected to look alike. Each is supposed to be that same speech cleaned into written prose: the same content, in the speaker's register and words, with nothing summarised and nothing added.

        Your task is to find, in each rewrite, every place where it changes what the speaker meant. Compare meaning, not style. A rewrite may read very differently from the raw and still be faithful; a rewrite may change one word and betray the speaker. Judge every rewrite on its own against the raw: never compare rewrites to each other, and never let a finding in one rewrite decide a finding in another. A change present in three rewrites is reported three times, once under each.
        """;

    const string BatchSchema = """
        Answer with one JSON object and nothing else: no prose, no code fence. It holds one entry per rewrite, in the order given, each carrying the rewrite's number. Use empty arrays when a category has no finding. The schema:

        {
          "rewrites": [
            {
              "n": 1,
              "term_substitutions": [{"raw": "...", "rewrite": "...", "note": "..."}],
              "meaning_inversions": [{"raw": "...", "rewrite": "...", "note": "..."}],
              "hedges_dropped":     [{"raw": "...", "rewrite": "...", "note": "..."}],
              "omissions":          [{"raw": "...", "note": "..."}],
              "additions":          [{"rewrite": "...", "note": "..."}],
              "verdict": "faithful" | "minor" | "unfaithful"
            }
          ]
        }
        """;

    public static string SystemPrompt(string? profileNote)
    {
        var sb = new StringBuilder();
        sb.Append(BatchIntro);
        if (!string.IsNullOrWhiteSpace(profileNote))
        {
            sb.Append("\n\nAbout the rewrite profile under test: ").Append(profileNote.Trim());
        }
        sb.Append("\n\n").Append(Judge.CategoryRules);
        sb.Append("\n\n").Append(Judge.VerdictRule);
        sb.Append("\n\n").Append(BatchSchema);
        return sb.ToString();
    }

    public static BatchJudgementRow JudgeBatch(
        AnthropicClient client, string judgeModel, string? profileNote,
        string key, string rawText, IReadOnlyList<GenerationRow> batch, CancellationToken cancellationToken)
    {
        // The raw comes first and is cached: every batch of the cell, and the
        // same dictation's batches for the other models, start with it.
        string head = $"<raw>\n{rawText}\n</raw>";
        var tail = new StringBuilder();
        tail.Append("\n\n<rewrites>\n");
        for (int i = 0; i < batch.Count; i++)
            tail.Append($"<rewrite n=\"{i + 1}\">\n").Append(batch[i].Text).Append("\n</rewrite>\n");
        tail.Append("</rewrites>");

        var parameters = new MessageCreateParams
        {
            Model = judgeModel,
            MaxTokens = MaxOutputTokens,
            System = PromptCache.System(SystemPrompt(profileNote)),
            OutputConfig = new OutputConfig { Effort = Effort.High },
            Messages = [new() { Role = Role.User, Content = PromptCache.User(head, tail.ToString()) }],
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Message? response = null;
        string? error = null;
        for (int attempt = 1; attempt <= 2 && response is null; attempt++)
        {
            try
            {
                response = client.Messages.Create(parameters, cancellationToken: cancellationToken).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (attempt == 1 && Transient.Is(ex))
            {
                Console.WriteLine($"  transient failure, retrying once: {Transient.Describe(ex)}");
            }
            catch (Exception ex)
            {
                error = Transient.Describe(ex);
            }
        }
        sw.Stop();

        int[] iterations = batch.Select(g => g.Iteration ?? 0).ToArray();
        string model = batch[0].Model, prompt = batch[0].Prompt, id = batch[0].Id;

        BatchJudgementRow Row(List<Judgement?>? judgements, string? judgeRaw, string? err) => new(
            key, id, model, prompt, iterations, judgeModel, judgements, judgeRaw,
            (int?)response?.Usage.InputTokens, (int?)response?.Usage.OutputTokens, sw.ElapsedMilliseconds,
            response is null ? null : Pricing.Cost(judgeModel, response.Usage),
            err,
            (int?)response?.Usage.CacheCreationInputTokens, (int?)response?.Usage.CacheReadInputTokens);

        if (response is null) return Row(null, null, error ?? "no response");
        if (response.StopReason == "refusal") return Row(null, null, "refusal");

        var text = new StringBuilder();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out TextBlock? textBlock))
                text.Append(textBlock.Text);
        }
        string answer = text.ToString().Trim();

        var parsed = Parse(answer, batch.Count);
        return parsed is null
            ? Row(null, answer, response.StopReason == "max_tokens" ? "max_tokens" : "unparsed")
            : Row(parsed, null, null);
    }

    // The entries are matched by their "n", not by position, so a judge that
    // skipped or reordered one leaves that slot null rather than shifting the
    // verdicts of the others onto the wrong rewrites.
    static List<Judgement?>? Parse(string answer, int count)
    {
        int start = answer.IndexOf('{');
        int end = answer.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        BatchAnswer? parsed;
        try { parsed = JsonSerializer.Deserialize<BatchAnswer>(answer[start..(end + 1)], Json.Input); }
        catch (JsonException) { return null; }
        if (parsed?.Rewrites is null) return null;

        var slots = new List<Judgement?>(new Judgement?[count]);
        for (int i = 0; i < parsed.Rewrites.Count; i++)
        {
            var item = parsed.Rewrites[i];
            int n = item.N ?? (i + 1);
            if (n < 1 || n > count) continue;
            slots[n - 1] = new Judgement(item.TermSubstitutions, item.MeaningInversions, item.HedgesDropped, item.Omissions, item.Additions, item.Verdict);
        }
        return slots;
    }

    sealed record BatchAnswer(List<BatchItem>? Rewrites);

    sealed record BatchItem(
        int? N,
        List<Finding>? TermSubstitutions,
        List<Finding>? MeaningInversions,
        List<Finding>? HedgesDropped,
        List<Finding>? Omissions,
        List<Finding>? Additions,
        string? Verdict);
}
