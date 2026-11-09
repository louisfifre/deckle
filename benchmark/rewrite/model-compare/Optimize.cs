using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace Deckle.Benchmark.ModelCompare;

// ─── optimize: rewrite a profile's prompt until its score meets a target ─────
//
// A campaign measures prompts; this loop improves one. Each round runs the
// current best prompt's challenger on the same training dictations, has the
// judge audit every rewrite, scores the round, and asks a proposer model to
// write the next candidate from the prompt, the score and the judge's
// findings merged across the rewrites (the same slip in 18 of 20 rewrites is
// one line with its count, which is what a prompt rule can fix). A candidate
// that scores above the best so far becomes the best; the others stay in the
// history the proposer reads, so it does not try them twice.
//
// The score is plain: a faithful rewrite is 100, a minor one 50, an
// unfaithful one 0, and a call the API refused is 0 as well, because the
// app's user gets nothing. A rewrite longer than its raw, or carrying
// markdown marks or separators, loses 10 points: every live prompt forbids
// both. The round's score is the mean over its planned rewrites. With twenty
// rewrites per round the score moves by several points on sampling alone, so
// the loop's own best is an optimistic reading; the held-out validation at
// the end, baseline against best on dictations the loop never saw, is the
// number to quote.
//
// Resumable like a campaign: every round is a campaign folder, every
// finished round is a ledger line, every candidate prompt is a file written
// before its round starts. A restart reads the ledger, skips finished rounds,
// resumes the unfinished one and never re-asks the proposer for a prompt it
// already has. The budget is a hard ceiling checked before each round with
// the previous round's cost as the estimate, and a reserve keeps the
// validation payable.

sealed record OptimizeSpec(
    string Name,
    string Profile,
    string Model,
    string[] Buckets,
    string? JudgeNote,
    int TakePerBucket,
    int IterationsPerRound,
    int MaxRounds,
    double TargetScore,
    double BudgetUsd,
    double ValidationReserveUsd,
    int ValidationIterations,
    string ProposerModel,
    CampaignJudge Judge,
    string? StartPromptFile);

// The dictations frozen for the whole loop: the training set every round
// runs on, and the held-out set only the validation sees.
sealed record OptimizeDictations(Dictionary<string, string[]> Train, Dictionary<string, string[]> HeldOut);

sealed record Proposal(string Rationale, string Prompt, int? PromptTokens, int? EvalTokens, double? CostUsd, string ProposerModel);

sealed record RoundScore(
    int Planned, int Ok, int Errors, int Judged,
    int Faithful, int Minor, int Unfaithful,
    double WeightedFindings, double WordRatioMean, int FormatViolations,
    double GenerationCostUsd, double JudgeCostUsd, double Score);

sealed record LedgerRow(
    int Round, string PromptHash, string PromptPath, RoundScore Result, bool Accepted,
    double? ProposerCostUsd, string? Rationale, DateTimeOffset FinishedAt);

static class Optimize
{
    const int ProposerMaxOutputTokens = 16_000;
    const int MaxFindingLines = 80;
    const int MaxPromptChars = 8_000;

    public static int Run(string specPath, string? outDir, string apiKey)
    {
        var spec = JsonSerializer.Deserialize<OptimizeSpec>(File.ReadAllText(specPath), Json.Input)
            ?? throw new ArgumentException($"{specPath} holds no optimize spec.");
        Validate(spec);
        outDir ??= Path.Combine("results", $"opt-{spec.Name}");
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(Path.Combine(outDir, "prompts"));

        var raw = Corpus.LoadRaw();
        var dictations = FreezeDictations(spec, outDir, raw);
        var ledger = LoadLedger(outDir);
        var client = new AnthropicClient { ApiKey = apiKey };

        Console.WriteLine($"optimize={spec.Name} profile={spec.Profile} model={spec.Model} proposer={spec.ProposerModel} judge={spec.Judge.Model}");
        Console.WriteLine($"  target={spec.TargetScore} budget=${spec.BudgetUsd:F2} (reserve ${spec.ValidationReserveUsd:F2} for validation) rounds<={spec.MaxRounds} rewrites/round={RewritesPerRound(spec, dictations.Train)}");
        foreach (var (bucket, ids) in dictations.Train)
            Console.WriteLine($"  train    {bucket,-10} {string.Join(", ", ids.Select(id => $"{Judge.Short(id)} ({raw[id].WordsIn} w)"))}");
        foreach (var (bucket, ids) in dictations.HeldOut)
            Console.WriteLine($"  held-out {bucket,-10} {string.Join(", ", ids.Select(id => $"{Judge.Short(id)} ({raw[id].WordsIn} w)"))}");
        if (ledger.Count > 0)
            Console.WriteLine($"  resuming after {ledger.Count} finished round(s), spent ${Spent(ledger):F2}, best {Best(ledger)?.Result.Score:F1} at round {Best(ledger)?.Round}");
        Console.WriteLine($"-> {Path.GetFullPath(outDir)}");
        Console.WriteLine();

        double lastRoundCost = ledger.Count > 0 ? RoundCost(ledger[^1]) : 0;
        string stopReason = "max rounds reached";

        for (int round = ledger.Count; round < spec.MaxRounds; round++)
        {
            var best = Best(ledger);
            if (best is not null && best.Result.Score >= spec.TargetScore)
            {
                stopReason = $"target {spec.TargetScore} reached at round {best.Round}";
                break;
            }
            double spent = Spent(ledger);
            if (spent + lastRoundCost > spec.BudgetUsd - spec.ValidationReserveUsd)
            {
                stopReason = $"budget: spent ${spent:F2}, next round ~${lastRoundCost:F2}, ceiling ${spec.BudgetUsd - spec.ValidationReserveUsd:F2} before the validation reserve";
                break;
            }

            // The candidate of this round: the start prompt at round 0, else
            // the proposer's answer, kept on disk before the round spends.
            string promptPath = Path.Combine(outDir, "prompts", $"round-{round}.md");
            string rationalePath = Path.Combine(outDir, "prompts", $"round-{round}.json");
            Proposal? proposal = null;
            if (!File.Exists(promptPath))
            {
                if (round == 0)
                {
                    var start = spec.StartPromptFile is { } file ? Corpus.LoadPromptFile(file) : Corpus.LoadProfilePrompt(spec.Profile);
                    File.WriteAllText(promptPath, start.SystemPrompt);
                    Console.WriteLine($"round 0: start prompt {start.Name} ({start.SystemPrompt.Length} chars)");
                }
                else
                {
                    proposal = Propose(client, spec, outDir, best!, ledger, raw);
                    File.WriteAllText(promptPath, proposal.Prompt);
                    File.WriteAllText(rationalePath, JsonSerializer.Serialize(proposal, Json.Pretty));
                    Console.WriteLine($"round {round}: proposer wrote {proposal.Prompt.Length} chars for ${proposal.CostUsd:F4}");
                    Console.WriteLine($"  rationale: {proposal.Rationale}");
                }
            }
            else if (File.Exists(rationalePath))
            {
                proposal = JsonSerializer.Deserialize<Proposal>(File.ReadAllText(rationalePath), Json.Input);
                Console.WriteLine($"round {round}: reusing the candidate already proposed");
            }
            string promptText = File.ReadAllText(promptPath);

            string roundDir = Path.Combine(outDir, $"round-{round}");
            var resolved = CampaignFile.Freeze(BuildCampaign(spec, dictations.Train, $"round-{round}", promptText, spec.IterationsPerRound), roundDir);
            Console.WriteLine($"round {round}: prompt {resolved.Prompts[spec.Profile].Hash}, {RewritesPerRound(spec, dictations.Train)} rewrites");
            CampaignRun.RunFrozen(resolved, raw, roundDir, apiKey);

            var result = Score(resolved, roundDir, raw);
            bool accepted = best is null || result.Score > best.Result.Score;
            var row = new LedgerRow(round, resolved.Prompts[spec.Profile].Hash, promptPath, result, accepted,
                proposal?.CostUsd, proposal?.Rationale, DateTimeOffset.Now);
            ledger.Add(row);
            File.AppendAllText(Path.Combine(outDir, "ledger.jsonl"), JsonSerializer.Serialize(row, Json.Output) + Environment.NewLine);
            lastRoundCost = RoundCost(row);

            Console.WriteLine();
            Console.WriteLine($"round {round}: score {result.Score:F1} ({(accepted ? "new best" : $"kept best {best!.Result.Score:F1} from round {best.Round}")})"
                + $" faithful={result.Faithful} minor={result.Minor} unfaithful={result.Unfaithful} errors={result.Errors} findings/rewrite={result.WeightedFindings:F2}"
                + $" cost ${RoundCost(row):F2} spent ${Spent(ledger):F2}");
            Console.WriteLine();
        }

        var finalBest = Best(ledger) ?? throw new InvalidOperationException("No round ran.");
        Console.WriteLine($"loop stopped: {stopReason}. best round {finalBest.Round} score {finalBest.Result.Score:F1}, spent ${Spent(ledger):F2}");
        File.WriteAllText(Path.Combine(outDir, "best-prompt.md"), File.ReadAllText(finalBest.PromptPath));

        var validation = ValidateHeldOut(spec, outDir, dictations, ledger, raw, apiKey);
        WriteReport(spec, outDir, dictations, ledger, validation, stopReason, raw);
        return 0;
    }

    static void Validate(OptimizeSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Profile) || string.IsNullOrWhiteSpace(spec.Model)) throw new ArgumentException("An optimize spec names its profile and model.");
        if (spec.Buckets is not { Length: > 0 }) throw new ArgumentException("An optimize spec names at least one bucket.");
        if (spec.TakePerBucket < 1 || spec.IterationsPerRound < 1 || spec.MaxRounds < 1) throw new ArgumentException("take_per_bucket, iterations_per_round and max_rounds are at least 1.");
        if (spec.Judge is null || spec.Judge.Batch < 1) throw new ArgumentException("An optimize spec names its judge model and batch size.");
        if (string.IsNullOrWhiteSpace(spec.ProposerModel)) throw new ArgumentException("An optimize spec names its proposer model.");
        if (spec.BudgetUsd <= 0) throw new ArgumentException("budget_usd is positive.");
    }

    // ── the matrix of one round ──────────────────────────────────────────────

    static OptimizeDictations FreezeDictations(OptimizeSpec spec, string outDir, Dictionary<string, RawDictation> raw)
    {
        string path = Path.Combine(outDir, "dictations.json");
        if (File.Exists(path))
            return JsonSerializer.Deserialize<OptimizeDictations>(File.ReadAllText(path), Json.Input)
                ?? throw new InvalidOperationException($"{path} is unreadable.");

        var train = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var heldOut = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string bucket in spec.Buckets)
        {
            train[bucket] = CampaignFile.PickTypical(raw, bucket, spec.TakePerBucket);
            heldOut[bucket] = CampaignFile.PickTypical(raw, bucket, spec.TakePerBucket, train[bucket].ToHashSet(StringComparer.OrdinalIgnoreCase));
        }
        var frozen = new OptimizeDictations(train, heldOut);
        File.WriteAllText(path, JsonSerializer.Serialize(frozen, Json.Pretty));
        return frozen;
    }

    static ResolvedCampaign BuildCampaign(OptimizeSpec spec, Dictionary<string, string[]> dictations, string name, string promptText, int iterations)
    {
        var campaign = new Campaign(
            Name: $"{spec.Name}-{name}",
            Models: [spec.Model],
            Iterations: iterations,
            TakePerBucket: spec.TakePerBucket,
            Cells: [new CampaignCell(spec.Profile, spec.Buckets, spec.JudgeNote)],
            Dictations: dictations,
            Judge: spec.Judge);
        var prompts = new Dictionary<string, FrozenPrompt>(StringComparer.OrdinalIgnoreCase)
        {
            [spec.Profile] = new FrozenPrompt(spec.Profile, $"candidate:{name}", CampaignFile.Hash(promptText), promptText),
        };
        return new ResolvedCampaign(campaign, dictations, prompts, DateTimeOffset.Now);
    }

    static int RewritesPerRound(OptimizeSpec spec, Dictionary<string, string[]> dictations) =>
        spec.Buckets.Sum(b => dictations[b].Length) * spec.IterationsPerRound;

    // ── the score ────────────────────────────────────────────────────────────

    sealed record Joined(GenerationRow Generation, JudgementRow? Judgement);

    static List<Joined> LoadRows(string runDir)
    {
        string genPath = Path.Combine(runDir, "generations.jsonl");
        string judgePath = Path.Combine(runDir, "judgements.jsonl");
        var generations = File.Exists(genPath)
            ? Json.ReadLines<GenerationRow>(genPath).GroupBy(CampaignRun.GenKey).Select(g => g.Last()).ToList()
            : [];
        var judgements = File.Exists(judgePath)
            ? Json.ReadLines<JudgementRow>(judgePath)
                .GroupBy(j => $"{j.Id}|{j.Model}|{j.Prompt}|{j.Iteration}").Select(g => g.Last())
                .ToDictionary(j => $"{j.Id}|{j.Model}|{j.Prompt}|{j.Iteration}", StringComparer.Ordinal)
            : new Dictionary<string, JudgementRow>(StringComparer.Ordinal);
        return generations
            .Select(g => new Joined(g, judgements.TryGetValue(CampaignRun.GenKey(g), out var j) ? j : null))
            .ToList();
    }

    public static RoundScore Score(ResolvedCampaign resolved, string runDir, Dictionary<string, RawDictation> raw)
    {
        var rows = LoadRows(runDir);
        int planned = resolved.Campaign.Cells.Sum(c => c.Buckets.Sum(b => resolved.Dictations[b].Length))
            * resolved.Campaign.Arms.Length * resolved.Campaign.Iterations;

        var points = new List<double>();
        int faithful = 0, minor = 0, unfaithful = 0, judged = 0, violations = 0;
        double weighted = 0;
        var ratios = new List<double>();

        foreach (var row in rows)
        {
            var g = row.Generation;
            if (g.Error is not null || g.Text is null)
            {
                points.Add(0); // the user got nothing: a refusal is a failed rewrite
                continue;
            }
            var judgement = row.Judgement?.Judgement;
            if (judgement is null) continue; // not judged yet, or judge error: outside the mean

            double value = judgement.Verdict?.ToLowerInvariant() switch
            {
                "faithful" => 100,
                "minor" => 50,
                "unfaithful" => 0,
                _ => double.NaN,
            };
            if (double.IsNaN(value)) continue;
            judged++;
            if (value == 100) faithful++; else if (value == 50) minor++; else unfaithful++;
            weighted += Weighted(judgement);

            if (raw.TryGetValue(g.Id, out var d))
            {
                var m = Markers.Delta(d.Text, g.Text);
                var s = Structure.Of(d.Text, g.Text);
                ratios.Add(m.WordRatio);
                bool violated = false;
                if (m.WordRatio > 1.0) { value -= 10; violated = true; }
                if (s.MarkdownMarks > 0 || s.Separators > 0) { value -= 10; violated = true; }
                if (violated) violations++;
            }
            points.Add(Math.Max(0, value));
        }

        var ok = rows.Where(r => r.Generation.Error is null).ToList();
        return new RoundScore(
            Planned: planned,
            Ok: ok.Count,
            Errors: rows.Count - ok.Count,
            Judged: judged,
            Faithful: faithful, Minor: minor, Unfaithful: unfaithful,
            WeightedFindings: judged == 0 ? 0 : weighted / judged,
            WordRatioMean: ratios.Count == 0 ? 0 : ratios.Average(),
            FormatViolations: violations,
            GenerationCostUsd: rows.Sum(r => r.Generation.CostUsd ?? 0),
            JudgeCostUsd: rows.Sum(r => r.Judgement?.JudgeCostUsd ?? 0),
            Score: points.Count == 0 ? 0 : points.Average());
    }

    // The diagnostic beside the verdict: an inversion costs the reader more
    // than a dropped hedge, so the categories are not counted alike.
    static double Weighted(Judgement j) =>
        3 * (j.MeaningInversions?.Count ?? 0) + 2 * (j.TermSubstitutions?.Count ?? 0)
        + (j.HedgesDropped?.Count ?? 0) + (j.Omissions?.Count ?? 0) + (j.Additions?.Count ?? 0);

    // ── the ledger ───────────────────────────────────────────────────────────

    static List<LedgerRow> LoadLedger(string outDir)
    {
        string path = Path.Combine(outDir, "ledger.jsonl");
        return File.Exists(path) ? Json.ReadLines<LedgerRow>(path) : [];
    }

    static LedgerRow? Best(List<LedgerRow> ledger) => ledger.Where(r => r.Accepted).LastOrDefault();
    static double RoundCost(LedgerRow r) => r.Result.GenerationCostUsd + r.Result.JudgeCostUsd + (r.ProposerCostUsd ?? 0);
    static double Spent(List<LedgerRow> ledger) => ledger.Sum(RoundCost);

    // ── the proposer ─────────────────────────────────────────────────────────

    const string ProposerSystem = """
        You improve the system prompt of a dictation-rewrite profile. The profile belongs to a Windows utility that transcribes speech and rewrites the transcript into written prose. The prompt is sent as the system message to the generating model named below; the user message is the raw French speech-recognition transcript, and the model's whole answer replaces it on the user's screen. The prompt is written in French and read only by that model.

        An independent judge audits every rewrite against its raw transcript and reports findings in five categories: term_substitutions (a name, identifier or technical term replaced by another word), meaning_inversions (a negation, comparison, condition, logical link, or who-does-what changed), hedges_dropped (a marker of uncertainty turned into a plain assertion), omissions (an idea, example, number, name, reason or qualification missing) and additions (content with no counterpart in the raw). Normal cleaning (fillers, false starts, repeats, punctuation, paragraphs, obvious recognition errors) is never a finding. The verdict is "faithful" with no finding, "minor" when a reader would take away the same content, "unfaithful" when a claim, decision, needed term or the speaker's certainty changed.

        The score you must raise is the mean over the planned rewrites of: 100 for a faithful rewrite, 50 for a minor one, 0 for an unfaithful one, 0 for a call the API refused (the model produced no text), minus 10 when the rewrite is longer than its raw or carries markdown marks or separators.

        You receive the profile's purpose, the generating model, the current best prompt, its score and verdict counts, the judge's findings merged across the rewrites of that round (the same change found in several rewrites is one line with its count: a change found in most rewrites is systematic and a rule can fix it; one found once may be noise), and the history of earlier candidates with their scores and rationales.

        Write one revised prompt that raises the score. Rules:
        - Keep the profile's purpose exactly as stated: what it may reorder, merge or recompose stays allowed, what it must keep stays required.
        - Keep the prompt in French, as plain text the model reads; the register and length of the current prompt are a guide, not a limit, but stay under 7000 characters: a longer prompt is not a better one, and a rule the model ignores is better cut than repeated.
        - Write rules that generalise to any dictation. Never copy a span from the findings into the prompt, never name or allude to the topics, people, projects, products or examples of the dictations; state the general rule a finding illustrates. A short invented example is fine when it does not come from the findings.
        - Change what the findings justify and keep what works. A candidate that scored lower than the best is in the history: do not propose it again, and do not undo the changes that raised the score.
        - If calls were refused, the prompt itself is what the model declined: consider what in its wording could read as asking for something other than cleaning the user's own dictation, and reword it; do not add disclaimers or meta-commentary.
        - Do not ask the model for any output other than the rewritten text.

        Answer with one JSON object and nothing else: no prose, no code fence. The schema:

        {"rationale": "<English, three to six sentences: what you changed and why, tied to the findings>", "prompt": "<the full revised prompt in French>"}
        """;

    static Proposal Propose(AnthropicClient client, OptimizeSpec spec, string outDir, LedgerRow best, List<LedgerRow> ledger, Dictionary<string, RawDictation> raw)
    {
        string bestPrompt = File.ReadAllText(best.PromptPath);
        string roundDir = Path.Combine(outDir, $"round-{best.Round}");
        var rows = LoadRows(roundDir);
        var r = best.Result;

        var user = new StringBuilder();
        user.Append("<profile>\n")
            .Append($"Name: {spec.Profile}\n")
            .Append($"Purpose, as told to the judge: {spec.JudgeNote ?? "(none given)"}\n")
            .Append($"Generating model: {spec.Model}\n")
            .Append("</profile>\n\n");
        user.Append("<current_prompt>\n").Append(bestPrompt).Append("\n</current_prompt>\n\n");
        user.Append("<results>\n")
            .Append($"Round {best.Round}: score {r.Score:F1} / 100 over {r.Planned} planned rewrites; target {spec.TargetScore}.\n")
            .Append($"Verdicts: faithful {r.Faithful}, minor {r.Minor}, unfaithful {r.Unfaithful}; {r.Errors} call(s) refused or failed (no text, scored 0).\n")
            .Append($"Mean word ratio rewrite/raw {r.WordRatioMean:F2}; {r.FormatViolations} rewrite(s) penalised for length or markdown.\n")
            .Append($"Weighted findings per judged rewrite: {r.WeightedFindings:F2} (inversion 3, term 2, hedge 1, omission 1, addition 1).\n")
            .Append("</results>\n\n");

        user.Append("<findings>\n");
        var digest = rows
            .Where(x => x.Judgement?.Judgement is not null)
            .SelectMany(x => x.Judgement!.Judgement!.Categories().SelectMany(c => c.Items.Select(f => (c.Category, f))))
            .GroupBy(t => (t.Category, Raw: Norm(t.f.Raw), Rewrite: Norm(t.f.Rewrite)))
            .Select(g => new { g.Key.Category, g.Key.Raw, g.Key.Rewrite, Count = g.Count(), Note = g.First().f.Note })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Category)
            .Take(MaxFindingLines)
            .ToList();
        if (digest.Count == 0) user.Append("(no finding)\n");
        foreach (var f in digest)
        {
            user.Append($"- [{f.Category} x{f.Count}/{r.Judged}]");
            if (f.Raw.Length > 0) user.Append($" raw: «{f.Raw}»");
            if (f.Rewrite.Length > 0) user.Append($" rewrite: «{f.Rewrite}»");
            if (!string.IsNullOrWhiteSpace(f.Note)) user.Append($" — {f.Note!.Trim()}");
            user.Append('\n');
        }
        user.Append("</findings>\n\n");

        user.Append("<history>\n");
        foreach (var row in ledger)
        {
            user.Append($"round {row.Round}: score {row.Result.Score:F1}, faithful {row.Result.Faithful}/{row.Result.Minor}/{row.Result.Unfaithful}, errors {row.Result.Errors}; ")
                .Append(row.Round == 0 ? "the live prompt" : row.Accepted ? "accepted as best" : "rejected")
                .Append(row.Rationale is { } why ? $". Rationale: {Clip(why, 400)}" : "")
                .Append('\n');
        }
        user.Append("</history>");

        var parameters = new MessageCreateParams
        {
            Model = spec.ProposerModel,
            MaxTokens = ProposerMaxOutputTokens,
            System = ProposerSystem,
            OutputConfig = new OutputConfig { Effort = Effort.High },
            Messages = [new() { Role = Role.User, Content = user.ToString() }],
        };

        Exception? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            Message response;
            try
            {
                response = client.Messages.Create(parameters, cancellationToken: cts.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (Transient.Is(ex) && attempt < 3)
            {
                Console.WriteLine($"  proposer: transient failure, retrying: {Transient.Describe(ex)}");
                last = ex;
                continue;
            }
            if (response.StopReason == "refusal") throw new InvalidOperationException("The proposer refused the request.");

            var text = new StringBuilder();
            foreach (var block in response.Content)
                if (block.TryPickText(out TextBlock? tb)) text.Append(tb.Text);
            string answer = text.ToString();
            double? cost = Pricing.Cost(spec.ProposerModel, (int)response.Usage.InputTokens, (int)response.Usage.OutputTokens);

            var parsed = ParseProposal(answer);
            if (parsed is { Prompt.Length: > 200 } && parsed.Prompt.Length <= MaxPromptChars)
                return parsed with { PromptTokens = (int)response.Usage.InputTokens, EvalTokens = (int)response.Usage.OutputTokens, CostUsd = cost, ProposerModel = spec.ProposerModel };

            Console.WriteLine($"  proposer: answer unusable on attempt {attempt} ({(parsed is null ? "no JSON" : $"prompt of {parsed.Prompt.Length} chars")}), ${cost:F4} spent");
            File.WriteAllText(Path.Combine(outDir, "prompts", $"proposer-failed-{DateTime.Now:yyyyMMdd-HHmmss}.txt"), answer);
        }
        throw new InvalidOperationException("The proposer gave no usable prompt in three attempts.", last);
    }

    static Proposal? ParseProposal(string answer)
    {
        int start = answer.IndexOf('{');
        int end = answer.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            var doc = JsonDocument.Parse(answer[start..(end + 1)]);
            string? rationale = doc.RootElement.TryGetProperty("rationale", out var r) ? r.GetString() : null;
            string? prompt = doc.RootElement.TryGetProperty("prompt", out var p) ? p.GetString() : null;
            return prompt is null ? null : new Proposal(rationale ?? "", prompt.Trim(), null, null, null, "");
        }
        catch (JsonException) { return null; }
    }

    static string Norm(string? s) => (s ?? "").Trim();
    static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    // ── validation on held-out dictations ────────────────────────────────────

    sealed record ValidationRun(string Label, int Round, string Hash, RoundScore Result);

    static List<ValidationRun> ValidateHeldOut(OptimizeSpec spec, string outDir, OptimizeDictations dictations, List<LedgerRow> ledger, Dictionary<string, RawDictation> raw, string apiKey)
    {
        var runs = new List<ValidationRun>();
        var best = Best(ledger)!;
        var baseline = ledger[0];

        // What runs on the held-out set: the live profile prompt when the
        // loop started from a file (so the comparison the app's user needs,
        // live against best, is in the same table), the start prompt, and
        // the best when a candidate beat the start.
        var candidates = new List<(string Label, int Round, string Text)>();
        if (spec.StartPromptFile is not null)
            candidates.Add(("live", -1, Corpus.LoadProfilePrompt(spec.Profile).SystemPrompt));
        candidates.Add(("baseline", baseline.Round, File.ReadAllText(baseline.PromptPath)));
        if (best.Round != baseline.Round)
            candidates.Add(($"best-round-{best.Round}", best.Round, File.ReadAllText(best.PromptPath)));
        if (candidates.Count == 1)
        {
            Console.WriteLine("validation: no candidate beat the start prompt, nothing to validate");
            return runs;
        }

        int perRound = RewritesPerRound(spec, dictations.Train);
        int perValidation = RewritesPerRound(spec, dictations.HeldOut) * spec.ValidationIterations / spec.IterationsPerRound;
        double estimate = candidates.Count * (ledger.Average(RoundCost) * perValidation / Math.Max(1, perRound));
        if (Spent(ledger) + estimate > spec.BudgetUsd)
        {
            Console.WriteLine($"validation: skipped, ~${estimate:F2} would pass the ${spec.BudgetUsd:F2} budget (spent ${Spent(ledger):F2})");
            return runs;
        }

        // The best's folder carries its round: a loop resumed with a higher
        // target may crown a later round, and a frozen folder named "best"
        // would validate the old prompt again.
        foreach (var (label, round, text) in candidates)
        {
            string dir = Path.Combine(outDir, "validation", label);
            var resolved = CampaignFile.Freeze(BuildCampaign(spec, dictations.HeldOut, $"validation-{label}", text, spec.ValidationIterations), dir);
            Console.WriteLine($"validation {label}: prompt {resolved.Prompts[spec.Profile].Hash} on held-out dictations");
            CampaignRun.RunFrozen(resolved, raw, dir, apiKey);
            var result = Score(resolved, dir, raw);
            runs.Add(new ValidationRun(label, round, resolved.Prompts[spec.Profile].Hash, result));
            Console.WriteLine($"validation {label}: score {result.Score:F1} faithful={result.Faithful} minor={result.Minor} unfaithful={result.Unfaithful} errors={result.Errors} cost ${result.GenerationCostUsd + result.JudgeCostUsd:F2}");
        }
        File.WriteAllText(Path.Combine(outDir, "validation", "scores.json"), JsonSerializer.Serialize(runs, Json.Pretty));
        return runs;
    }

    // ── the report ───────────────────────────────────────────────────────────

    static void WriteReport(OptimizeSpec spec, string outDir, OptimizeDictations dictations, List<LedgerRow> ledger, List<ValidationRun> validation, string stopReason, Dictionary<string, RawDictation> raw)
    {
        var best = Best(ledger)!;
        var sb = new StringBuilder();
        sb.AppendLine($"# optimize {spec.Name} — {spec.Profile} on {spec.Model}");
        sb.AppendLine();
        sb.AppendLine($"Proposer {spec.ProposerModel}, judge {spec.Judge.Model} (batch {spec.Judge.Batch}), {spec.IterationsPerRound} iteration(s) per dictation per round, target {spec.TargetScore}, budget ${spec.BudgetUsd:F2}.");
        sb.AppendLine($"Stopped: {stopReason}. Spent in rounds ${Spent(ledger):F2}; validation ${validation.Sum(v => v.Result.GenerationCostUsd + v.Result.JudgeCostUsd):F2}.");
        sb.AppendLine();
        sb.AppendLine("Score: mean over planned rewrites of faithful 100 / minor 50 / unfaithful 0 / refused 0, minus 10 for a rewrite longer than its raw or carrying markdown; weighted findings = 3 inversions + 2 terms + hedges + omissions + additions, per judged rewrite.");
        sb.AppendLine();
        sb.AppendLine("Training dictations: " + string.Join("; ", dictations.Train.Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value.Select(id => $"{Judge.Short(id)} ({raw[id].WordsIn} w)"))}")));
        sb.AppendLine("Held-out dictations: " + string.Join("; ", dictations.HeldOut.Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value.Select(id => $"{Judge.Short(id)} ({raw[id].WordsIn} w)"))}")));
        sb.AppendLine();
        sb.AppendLine("## Rounds");
        sb.AppendLine();
        sb.AppendLine("| round | hash | score | faithful | minor | unfaithful | errors | findings/rewrite | word ratio | cost USD | best |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in ledger)
            sb.AppendLine($"| {r.Round} | {r.PromptHash} | {r.Result.Score:F1} | {r.Result.Faithful} | {r.Result.Minor} | {r.Result.Unfaithful} | {r.Result.Errors} | {r.Result.WeightedFindings:F2} | {r.Result.WordRatioMean:F2} | {RoundCost(r):F2} | {(r.Accepted ? "yes" : "")} |");
        sb.AppendLine();
        foreach (var r in ledger.Where(r => r.Rationale is not null))
            sb.AppendLine($"- round {r.Round} ({(r.Accepted ? "accepted" : "rejected")}): {r.Rationale}");
        sb.AppendLine();
        sb.AppendLine("## Validation on held-out dictations");
        sb.AppendLine();
        if (validation.Count == 0) sb.AppendLine("(not run)");
        else
        {
            sb.AppendLine("| prompt | round | hash | score | faithful | minor | unfaithful | errors | findings/rewrite | word ratio |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var v in validation)
                sb.AppendLine($"| {v.Label} | {v.Round} | {v.Hash} | {v.Result.Score:F1} | {v.Result.Faithful} | {v.Result.Minor} | {v.Result.Unfaithful} | {v.Result.Errors} | {v.Result.WeightedFindings:F2} | {v.Result.WordRatioMean:F2} |");
        }
        sb.AppendLine();
        sb.AppendLine($"## Best prompt (round {best.Round}, hash {best.PromptHash})");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(File.ReadAllText(best.PromptPath));
        sb.AppendLine("```");
        File.WriteAllText(Path.Combine(outDir, "optimize-report.md"), sb.ToString());
        Console.WriteLine($"-> {Path.GetFullPath(Path.Combine(outDir, "optimize-report.md"))}");
    }
}
