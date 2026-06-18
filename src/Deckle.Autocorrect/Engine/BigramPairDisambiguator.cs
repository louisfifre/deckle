using System.IO;
using System.IO.Compression;
using System.Text;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect;

// ── PairModel ───────────────────────────────────────────────────────────────
//
// The in-memory shape of the left-context pair model, shared by the trainer
// (which fills it) and the disambiguator (which queries it). Layout:
//   folded → variant → prev → count,  prev="" = the per-slot unigram total.
//
// It is the serialization unit too: SaveTsvGz / LoadTsvGz round-trip the rows
// exactly, so a trained model on disk and the model that trained it are equal.
public sealed class PairModel
{
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, long>>> _model;

    public PairModel(
        Dictionary<string, Dictionary<string, Dictionary<string, long>>> model,
        TrainerReport? report = null)
    {
        _model = model;
        Report = report ?? new TrainerReport(0, 0, 0, CountRows(model));
    }

    // The training pass's counters when this model came from Train; a row count
    // otherwise.
    public TrainerReport Report { get; }

    // Distinct folded keys that hold any data.
    public int SlotCount => _model.Count;

    // Total rows (every folded/variant/prev triple, unigrams included).
    public long RowCount => CountRows(_model);

    // Bigram count for (folded, variant, prev). 0 when the row is absent.
    public long Bigram(string folded, string variant, string prev) =>
        _model.TryGetValue(folded, out var byVariant)
        && byVariant.TryGetValue(variant, out var byPrev)
        && byPrev.TryGetValue(prev, out long c) ? c : 0L;

    // Unigram total for (folded, variant) — the prev="" row. 0 when absent.
    public long Unigram(string folded, string variant) =>
        Bigram(folded, variant, string.Empty);

    // Every row as a (folded, variant, prev, count) tuple — for serialization.
    public IEnumerable<(string Folded, string Variant, string Prev, long Count)> Rows()
    {
        foreach (var (folded, byVariant) in _model)
            foreach (var (variant, byPrev) in byVariant)
                foreach (var (prev, count) in byPrev)
                    yield return (folded, variant, prev, count);
    }

    // Build a model from flat rows — the trainer→disambiguator handoff and the
    // deserialization path share this.
    public static PairModel FromRows(
        IEnumerable<(string Folded, string Variant, string Prev, long Count)> rows)
    {
        var model = new Dictionary<string, Dictionary<string, Dictionary<string, long>>>(StringComparer.Ordinal);
        foreach (var (folded, variant, prev, count) in rows)
        {
            if (!model.TryGetValue(folded, out var byVariant))
                model[folded] = byVariant = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
            if (!byVariant.TryGetValue(variant, out var byPrev))
                byVariant[variant] = byPrev = new Dictionary<string, long>(StringComparer.Ordinal);
            byPrev[prev] = byPrev.TryGetValue(prev, out long prior) ? prior + count : count;
        }
        return new PairModel(model);
    }

    // Write `folded<TAB>variant<TAB>prev<TAB>count`, UTF-8, gzip. prev may be
    // empty (the unigram row) — it is still its own tab-delimited field.
    public void SaveTsvGz(string path)
    {
        using var file = File.Create(path);
        using var gz = new GZipStream(file, CompressionLevel.Optimal);
        using var writer = new StreamWriter(gz, new UTF8Encoding(false));
        foreach (var (folded, variant, prev, count) in Rows())
            writer.Write($"{folded}\t{variant}\t{prev}\t{count}\n");
    }

    public static PairModel LoadTsvGz(string path)
    {
        using var file = File.OpenRead(path);
        using var gz = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        return LoadTsv(reader);
    }

    // Parse the flat TSV back into a model. A line must have exactly four
    // tab-delimited fields; prev (field 3) may be empty. Malformed lines are
    // skipped silently — the file is our own artifact, not user input.
    public static PairModel LoadTsv(TextReader reader)
    {
        var rows = new List<(string, string, string, long)>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
                continue;
            string[] parts = line.Split('\t');
            if (parts.Length != 4)
                continue;
            if (!long.TryParse(parts[3], out long count))
                continue;
            rows.Add((parts[0], parts[1], parts[2], count));
        }
        return FromRows(rows);
    }

    private static long CountRows(Dictionary<string, Dictionary<string, Dictionary<string, long>>> model)
    {
        long n = 0;
        foreach (var byVariant in model.Values)
            foreach (var byPrev in byVariant.Values)
                n += byPrev.Count;
        return n;
    }
}

// ── BigramPairDisambiguator ─────────────────────────────────────────────────
//
// Stage two of the engine, the context model: given the left context (up to two
// preceding words within the sentence) and the accent variants of one ambiguous
// folded form, it picks the variant the context favors — or returns null,
// leaving the literal untouched. Null is the expected, conservative outcome; a
// verdict is earned only when the evidence is both present and decisive.
//
// NOTE: this is now an n-gram backoff model (trigram → bigram → unigram), no
// longer a pure bigram. The class keeps its name until the trigram direction is
// confirmed by the offline eval, then renames.
//
// The decision is a guarded argmax over per-candidate scores:
//   • score(c) backs off per candidate: the trigram count for the "prevPrev prev"
//     context if seen, else the bigram count for "prev", else the Unigram total;
//   • the bare folded form gets its score multiplied by LiteralBias — the
//     Gboard "cost to correct away from a valid word", here a defense: undoing
//     a form that is itself legal French must clear a higher bar;
//   • add-one smoothing on the compared scores so a zero never divides;
//   • the evidence gate: the raw scores must sum to at least MinEvidence, else
//     null — we never guess from thin air;
//   • the margin: winner.smoothed >= MarginRatio * runnerUp.smoothed, else null.
public sealed class BigramPairDisambiguator : IPairDisambiguator
{
    private readonly PairModel _model;
    private readonly DisambiguatorOptions _options;

    public BigramPairDisambiguator(PairModel model, DisambiguatorOptions? options = null)
    {
        _model = model;
        _options = options ?? new DisambiguatorOptions();
    }

    // Construct straight from flat rows — the trainer handoff and tests.
    public BigramPairDisambiguator(
        IEnumerable<(string Folded, string Variant, string Prev, long Count)> rows,
        DisambiguatorOptions? options = null)
        : this(PairModel.FromRows(rows), options)
    {
    }

    public static BigramPairDisambiguator LoadTsvGz(string path, DisambiguatorOptions? options = null) =>
        new(PairModel.LoadTsvGz(path), options);

    public int SlotCount => _model.SlotCount;
    public long RowCount => _model.RowCount;

    public string? Choose(
        IReadOnlyList<string> leftContext,
        IReadOnlyList<AccentVariant> candidates,
        StageTrace? trace = null)
    {
        if (candidates.Count < 2)
            return null;

        // All candidates fold to the same key — derive it once from the first.
        string folded = AccentFolding.Fold(candidates[0].Form);

        // Context keys, highest order first. Both honor MaxContextOrder, so the
        // model can be queried as a pure bigram (order 2) for the A/B baseline.
        // The words are already lowercased by the caller; the most recent is last.
        int n = leftContext.Count;
        string? bigramKey = _options.MaxContextOrder >= 2 && n >= 1
            ? leftContext[n - 1]
            : null;
        string? trigramKey = _options.MaxContextOrder >= 3 && n >= 2
            ? leftContext[n - 2] + " " + leftContext[n - 1]
            : null;

        // Raw score per candidate, backing off trigram → bigram → unigram. Sum
        // the raw scores for the evidence gate (before bias/smoothing — bias must
        // not manufacture evidence out of a literal that has none).
        double rawSum = 0.0;
        double bestScore = double.NegativeInfinity, secondScore = double.NegativeInfinity;
        string? bestForm = null;

        foreach (var c in candidates)
        {
            long raw;
            if (trigramKey is not null && _model.Bigram(folded, c.Form, trigramKey) is var t and > 0)
                raw = t;
            else if (bigramKey is not null && _model.Bigram(folded, c.Form, bigramKey) is var b and > 0)
                raw = b;
            else
                raw = _model.Unigram(folded, c.Form);
            rawSum += raw;

            // Add-one smoothing, then the literal defense on the bare form.
            double score = raw + 1.0;
            if (c.Form == folded)
                score *= _options.LiteralBias;

            if (score > bestScore)
            {
                secondScore = bestScore;
                bestScore = score;
                bestForm = c.Form;
            }
            else if (score > secondScore)
            {
                secondScore = score;
            }
        }

        // The context decision's safety gauges, for the decision telemetry: the
        // smoothed best/second scores, the margin they form against its threshold,
        // and the raw evidence against its floor. Recorded onto the caller's
        // (diacritics) stage trace before the gates below read them.
        if (trace is not null)
        {
            double margin = secondScore > 0.0 ? bestScore / secondScore : double.PositiveInfinity;
            trace.Gauge("ctx_best", bestScore)
                 .Gauge("ctx_second", secondScore)
                 .Gauge("ctx_margin", margin)
                 .Gauge("ctx_margin_min", _options.MarginRatio)
                 .Gauge("ctx_evidence", rawSum)
                 .Gauge("ctx_evidence_min", _options.MinEvidence);
        }

        // Never guess from thin air.
        if (rawSum < _options.MinEvidence)
            return null;

        // Require a clear margin — never a bare argmax.
        if (bestForm is null || bestScore < _options.MarginRatio * secondScore)
            return null;

        return bestForm;
    }
}

// Tuning for the context decision. Defaults are the measured optimum of the
// 2026-06-13 eval matrix (see the module JOURNAL): margin is the lever that
// kills wrong-variant picks (342→116 from 3× to 10× for −0.6 pt of recall);
// the evidence floor barely moves anything past 5.
public sealed record DisambiguatorOptions
{
    // The winner's smoothed score must beat the runner-up's by this factor.
    public double MarginRatio { get; init; } = 10.0;

    // Multiplier on the bare folded form's score — the cost of correcting away
    // from a word that is itself valid French.
    public double LiteralBias { get; init; } = 2.0;

    // Minimum total raw evidence (summed counts) before any verdict is allowed.
    public int MinEvidence { get; init; } = 5;

    // Highest context order the disambiguator will use: 3 = trigram backing off
    // to bigram then unigram; 2 reproduces the bigram model exactly (the A/B
    // baseline). Capped in effect by what the trained model carries.
    public int MaxContextOrder { get; init; } = 3;
}
