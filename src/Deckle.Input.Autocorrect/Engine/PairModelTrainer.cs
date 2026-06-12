using System.IO;
using Deckle.Input.Autocorrect.Lexicon;
using Deckle.Input.Autocorrect.Tracking;

namespace Deckle.Input.Autocorrect.Engine;

// ── PairModelTrainer ────────────────────────────────────────────────────────
//
// Offline builder of the left-context pair model the disambiguator reads. It
// walks a French corpus, finds the ambiguous slots — folded keys that map to
// two or more legal surface forms (the a/à kind) — and counts, per previous
// token, which variant the writer actually chose.
//
// A row is `folded<TAB>variant<TAB>prev<TAB>count`:
//   folded  = AccentFolding.Fold key of an ambiguous slot.
//   variant = one realized surface form (the bare folded form is first-class —
//             that is how "a" stays a candidate against "à").
//   prev    = lowercased previous token in the same sentence, "" at sentence
//             start AND for the per-slot unigram total (always present).
//
// The trainer must split words exactly like the live tracker: it tokenizes
// with WordBoundaries.Tokenize, so the prev it learns is the prev the engine
// will hand the disambiguator at runtime. Sentence splitting is deliberately
// terse — no NLP — because the only thing it gates is whether two tokens are
// close enough to count as a bigram.
public static class PairModelTrainer
{
    // Sentence terminators: hard stops plus the ellipsis and the clause breaks
    // (; :) that reset left context. Newlines are handled separately.
    private static readonly char[] SentenceBreaks = { '.', '!', '?', ';', ':', '…' };

    // Train a model from a corpus reader against the gate's own lexicon and
    // index — same notion of "ambiguous" as the runtime engine.
    public static PairModel Train(
        TextReader corpus,
        FrequencyLexicon french,
        AccentIndex index,
        TrainerOptions? options = null)
    {
        var opts = options ?? new TrainerOptions();

        // folded → variant → prev → count. The "" prev row is the unigram total.
        var model = new Dictionary<string, Dictionary<string, Dictionary<string, long>>>(StringComparer.Ordinal);

        long sentences = 0;
        long tokens = 0;
        long ambiguousOccurrences = 0;

        string? line;
        while ((line = corpus.ReadLine()) is not null)
        {
            foreach (string sentence in SplitSentences(line))
            {
                sentences++;
                string? prev = null; // null = sentence start; counted as "".

                foreach (string raw in WordBoundaries.Tokenize(sentence))
                {
                    // A token participates only if it carries a letter — digit
                    // and symbol tokens never reach the gate.
                    if (!HasLetter(raw))
                    {
                        prev = raw.ToLowerInvariant();
                        continue;
                    }

                    tokens++;
                    string token = raw.ToLowerInvariant();
                    string folded = AccentFolding.Fold(token);

                    if (IsAmbiguousAndContains(folded, token, french, index))
                    {
                        ambiguousOccurrences++;
                        string prevKey = prev ?? string.Empty;
                        Bump(model, folded, token, prevKey);   // the bigram
                        Bump(model, folded, token, string.Empty); // the unigram total
                    }

                    prev = token;
                }
            }
        }

        long keptRows = Prune(model, opts);
        return new PairModel(
            model,
            new TrainerReport(sentences, tokens, ambiguousOccurrences, keptRows));
    }

    // Train then write to a gz TSV in one call.
    public static TrainerReport TrainToFile(
        TextReader corpus,
        FrequencyLexicon french,
        AccentIndex index,
        string path,
        TrainerOptions? options = null)
    {
        PairModel model = Train(corpus, french, index, options);
        model.SaveTsvGz(path);
        return model.Report;
    }

    // A slot is ambiguous when the folded key resolves to 2+ legal surface
    // forms: every accented variant the index holds, plus the bare folded form
    // itself when French accepts it as a word (a, ou, …). We only count the
    // occurrence when the realized token is one of those candidates — a typo
    // that merely folds to an ambiguous key is not a data point.
    private static bool IsAmbiguousAndContains(
        string folded, string token, FrequencyLexicon french, AccentIndex index)
    {
        var variants = index.VariantsOf(folded);
        bool bareIsWord = french.Contains(folded);
        int candidateCount = variants.Count + (bareIsWord ? 1 : 0);
        if (candidateCount < 2)
            return false;

        if (token == folded)
            return bareIsWord;

        foreach (var v in variants)
            if (v.Form == token)
                return true;
        return false;
    }

    private static void Bump(
        Dictionary<string, Dictionary<string, Dictionary<string, long>>> model,
        string folded, string variant, string prev)
    {
        if (!model.TryGetValue(folded, out var byVariant))
            model[folded] = byVariant = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        if (!byVariant.TryGetValue(variant, out var byPrev))
            byVariant[variant] = byPrev = new Dictionary<string, long>(StringComparer.Ordinal);
        byPrev[prev] = byPrev.TryGetValue(prev, out long c) ? c + 1 : 1;
    }

    // Prune the bigram tail: drop (folded,variant,prev) rows below MinPairCount,
    // then keep only the MaxPrevPerSlot most frequent prevs per slot. The ""
    // unigram row is never pruned — it is the fallback the disambiguator leans
    // on. Returns the number of rows surviving (counting the unigram rows).
    private static long Prune(
        Dictionary<string, Dictionary<string, Dictionary<string, long>>> model,
        TrainerOptions opts)
    {
        long kept = 0;
        foreach (var byVariant in model.Values)
        {
            foreach (var byPrev in byVariant.Values)
            {
                // Drop low-count bigrams (never the unigram total).
                var toDrop = new List<string>();
                foreach (var (prev, count) in byPrev)
                    if (prev.Length != 0 && count < opts.MinPairCount)
                        toDrop.Add(prev);
                foreach (string prev in toDrop)
                    byPrev.Remove(prev);

                // Cap the prevs per (folded,variant) slot, keeping the heaviest.
                int bigrams = byPrev.Count - (byPrev.ContainsKey(string.Empty) ? 1 : 0);
                if (bigrams > opts.MaxPrevPerSlot)
                {
                    var ranked = new List<KeyValuePair<string, long>>();
                    foreach (var kv in byPrev)
                        if (kv.Key.Length != 0)
                            ranked.Add(kv);
                    ranked.Sort(static (a, b) => b.Value.CompareTo(a.Value));
                    for (int i = opts.MaxPrevPerSlot; i < ranked.Count; i++)
                        byPrev.Remove(ranked[i].Key);
                }

                kept += byPrev.Count;
            }
        }
        return kept;
    }

    // Split a line on the terse sentence breaks. The terminator is consumed;
    // blank or whitespace-only fragments (e.g. the tail after the last period)
    // are not sentences and yield nothing.
    private static IEnumerable<string> SplitSentences(string line)
    {
        int start = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (Array.IndexOf(SentenceBreaks, line[i]) >= 0)
            {
                string fragment = line[start..i];
                if (!string.IsNullOrWhiteSpace(fragment))
                    yield return fragment;
                start = i + 1;
            }
        }
        string tail = line[start..];
        if (!string.IsNullOrWhiteSpace(tail))
            yield return tail;
    }

    private static bool HasLetter(string token)
    {
        foreach (char c in token)
            if (char.IsLetter(c))
                return true;
        return false;
    }
}

// Tuning for the trainer's pruning. Defaults lean toward a compact model: the
// long tail of one-off bigrams is noise the disambiguator's evidence gate would
// reject anyway.
public sealed record TrainerOptions
{
    // Drop (folded,variant,prev) bigram rows seen fewer than this many times.
    // The "" unigram rows are always kept regardless.
    public int MinPairCount { get; init; } = 3;

    // Keep at most this many distinct prevs per (folded,variant) slot, the most
    // frequent ones; prune the tail.
    public int MaxPrevPerSlot { get; init; } = 64;
}

// What the training pass observed — a data-quality signal, not engine state.
public sealed record TrainerReport(
    long Sentences,
    long Tokens,
    long AmbiguousSlotOccurrences,
    long KeptRows);
