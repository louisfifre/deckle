using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace Deckle.Benchmark.ModelCompare;

// ─── The judge: an LLM reads raw and rewrite side by side ────────────────────
//
// The judge calls the SDK directly rather than through ClaudeEngine: the
// engine holds effort low because a rewrite is transcription work, while
// spotting a flipped negation in 1,300 words of speech is reasoning work.
// Thinking stays at the SDK default (adaptive on the current models) and
// effort is set high explicitly, because Opus 5.5 defaults to medium.
//
// No refusal fallback: a judgement silently produced by another model would
// enter the table under the wrong judge. A refusal is recorded as an error.
//
// The judge is a lead, not ground truth: its findings say where to read.

static class Judge
{
    // A judgement is short JSON, but adaptive thinking at high effort spends
    // output tokens first; 16 K leaves room for both on a long dictation and
    // stays under the SDK's non-streaming timeout.
    const int MaxOutputTokens = 16_000;

    // Assembled from parts: the batch judge (BatchJudge.cs) shares the
    // category rules and the verdict scale word for word, so a verdict means
    // the same thing whether the judge read one rewrite or five. Only the
    // framing and the answer schema differ.
    public const string SystemPrompt = SingleIntro + "\n\n" + CategoryRules + "\n\n" + VerdictRule + "\n\n" + SingleSchema;

    const string SingleIntro = """
        You audit the fidelity of an automatic rewrite of French dictated speech.

        The text inside <raw> is a speech-recognition transcript of one person speaking French. It carries everything speech carries: hesitations, fillers, false starts, fragments repeated by the flow of speech, dropped "ne", run-on sentences, and recognition errors. The text inside <rewrite> is supposed to be that same speech cleaned into written prose: the same content, in the same order, in the speaker's register and words, with nothing summarised and nothing added.

        Your task is to find every place where the rewrite changes what the speaker meant. Compare meaning, not style. A rewrite may read very differently from the raw and still be faithful; a rewrite may change one word and betray the speaker.
        """;

    public const string CategoryRules = """
        Report findings in five categories.

        term_substitutions: a proper noun, project name, tool or product name, identifier, or technical term in the raw that the rewrite replaced with a different word. Speech recognition often spells such terms phonetically or splits them into ordinary words. A term the rewrite leaves as the raw has it, however odd it looks, is correct and is not reported. A term restored to the name the speaker obviously meant is correct. A term turned into a common word, a different technical term, or another name that appears elsewhere in the text is a substitution, even when the result reads more naturally.

        meaning_inversions: a statement whose meaning changed direction. A negation added or removed, a comparison introduced, removed or reversed, a condition or its consequence flipped, a conjunction that changes the logical link between two ideas (cause, concession, opposition, consequence), or who does what to whom swapped. When the raw is ambiguous and the rewrite resolves it into a reading that says something the speaker did not clearly say, report it.

        hedges_dropped: a marker of uncertainty, opinion or tentativeness in the raw ("je pense que", "je crois", "peut-être", "il me semble", "j'ai l'impression", "je sais pas", "probablement", "a priori", a conditional verb) that has no equivalent in the matching part of the rewrite, so that a tentative statement becomes a plain assertion. A hedge kept in another form is not dropped.

        omissions: an idea, example, comparison, analogy, number, name, reason, condition or qualification present in the raw and absent from the rewrite. Not omissions: fillers, hesitations, false starts the speaker abandoned, self-corrections where only the corrected version should remain, and fragments repeated by the flow of speech (an idea said twice in the same words should appear once).

        additions: content in the rewrite with no counterpart in the raw: a claim, explanation, example, detail, or logical connection the speaker did not make. Grammatical words needed to turn speech into correct prose are not additions.

        Normal cleaning is never reported: removed fillers and hesitations, removed false starts and repetitions, restored "ne", punctuation, paragraphing, capitalisation, agreement and conjugation fixes, a recognition error corrected into the common word the context makes obvious, and light reordering inside a sentence that keeps its meaning.

        For each finding, quote the shortest span of the raw and the shortest span of the rewrite that show the change, copied exactly from the texts, in French, and add a short note in English saying what changed in meaning. Report every instance separately. Report each change once, in the most specific category: a dropped negation is a meaning inversion, not an omission.
        """;

    public const string VerdictRule = """
        Verdict: "faithful" when you report nothing; "minor" when every finding is small enough that a reader would take away the same content; "unfaithful" when at least one finding changes a claim, a decision, a term the reader needs, or the speaker's certainty on a point that matters.
        """;

    const string SingleSchema = """
        Answer with one JSON object and nothing else: no prose, no code fence. Use empty arrays when a category has no finding. The schema:

        {
          "term_substitutions": [{"raw": "...", "rewrite": "...", "note": "..."}],
          "meaning_inversions": [{"raw": "...", "rewrite": "...", "note": "..."}],
          "hedges_dropped":     [{"raw": "...", "rewrite": "...", "note": "..."}],
          "omissions":          [{"raw": "...", "note": "..."}],
          "additions":          [{"rewrite": "...", "note": "..."}],
          "verdict": "faithful" | "minor" | "unfaithful"
        }
        """;

    public static int Run(string inPath, string judgeModel, string apiKey)
    {
        var generations = Json.ReadLines<GenerationRow>(inPath);
        var raw = Corpus.LoadRaw();
        var client = new AnthropicClient { ApiKey = apiKey };
        string outPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inPath))!, "judgements.jsonl");

        Console.WriteLine($"judge={judgeModel} generations={generations.Count} -> {outPath}");
        Console.WriteLine();

        double total = 0;
        using var writer = new StreamWriter(outPath, append: false);
        foreach (var gen in generations)
        {
            if (gen.Error is not null || string.IsNullOrWhiteSpace(gen.Text))
            {
                Console.WriteLine($"{Short(gen.Id)} {gen.Model,-18} {gen.Prompt,-36} skipped (no generation)");
                continue;
            }
            if (!raw.TryGetValue(gen.Id, out var dictation))
            {
                Console.WriteLine($"{Short(gen.Id)} {gen.Model,-18} {gen.Prompt,-36} skipped (raw not in corpus)");
                continue;
            }

            var row = JudgeOne(client, judgeModel, gen, dictation.Text);
            total += row.JudgeCostUsd ?? 0;
            writer.WriteLine(JsonSerializer.Serialize(row, Json.Output));
            writer.Flush(); // a crash mid-run keeps every paid judgement so far

            string outcome = row.Error ?? row.Judgement?.Verdict ?? "unparsed";
            string counts = row.Judgement is { } j
                ? string.Join(" ", j.Categories().Select(c => $"{Abbrev(c.Category)}={c.Items.Count}"))
                : "";
            Console.WriteLine($"{Short(gen.Id)} {gen.Model,-18} {gen.Prompt,-36} {outcome,-11} {counts}  "
                + $"tok={row.JudgePromptTokens}/{row.JudgeEvalTokens} {row.JudgeMs}ms ${row.JudgeCostUsd:F4}");
        }

        Console.WriteLine();
        Console.WriteLine($"judge cost: ${total:F4}");
        return 0;
    }

    static JudgementRow JudgeOne(AnthropicClient client, string judgeModel, GenerationRow gen, string rawText)
    {
        var parameters = new MessageCreateParams
        {
            Model = judgeModel,
            MaxTokens = MaxOutputTokens,
            // The system prompt is the same for every call and the raw for
            // every rewrite of one dictation: both are cached ahead of the
            // rewrite, which changes each time.
            System = PromptCache.System(SystemPrompt),
            OutputConfig = new OutputConfig { Effort = Effort.High },
            Messages = [new() { Role = Role.User, Content = PromptCache.User($"<raw>\n{rawText}\n</raw>", $"\n\n<rewrite>\n{gen.Text}\n</rewrite>") }],
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Message? response = null;
        string? error = null;
        for (int attempt = 1; attempt <= 2 && response is null; attempt++)
        {
            try
            {
                response = client.Messages.Create(parameters).GetAwaiter().GetResult();
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

        JudgementRow Row(Judgement? judgement, string? judgeRaw, string? err) => new(
            gen.Id, gen.Model, gen.Prompt, gen.Source, judgeModel, judgement, judgeRaw,
            (int?)response?.Usage.InputTokens, (int?)response?.Usage.OutputTokens, sw.ElapsedMilliseconds,
            response is null ? null : Pricing.Cost(judgeModel, response.Usage),
            err,
            JudgeCacheWriteTokens: (int?)response?.Usage.CacheCreationInputTokens,
            JudgeCacheReadTokens: (int?)response?.Usage.CacheReadInputTokens);

        if (response is null) return Row(null, null, error ?? "no response");
        if (response.StopReason == "refusal") return Row(null, null, "refusal");

        var text = new System.Text.StringBuilder();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out TextBlock? textBlock))
                text.Append(textBlock.Text);
        }
        string answer = text.ToString().Trim();

        var parsed = Parse(answer);
        return parsed is null
            ? Row(null, answer, response.StopReason == "max_tokens" ? "max_tokens" : null)
            : Row(parsed, null, null);
    }

    // The prompt asks for bare JSON, but a code fence or a stray sentence
    // around it must not cost the judgement: take the outermost {...} span.
    public static Judgement? Parse(string answer)
    {
        int start = answer.IndexOf('{');
        int end = answer.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            return JsonSerializer.Deserialize<Judgement>(answer[start..(end + 1)], Json.Input);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Short(string id) => id.Length > 8 ? id[..8] : id;

    static string Abbrev(string category) => category switch
    {
        "term_substitutions" => "terms",
        "meaning_inversions" => "inv",
        "hedges_dropped" => "hedges",
        "omissions" => "omit",
        "additions" => "add",
        _ => category,
    };
}
