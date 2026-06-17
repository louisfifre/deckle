using System.IO;
using Deckle.Input.Autocorrect;
using Deckle.Input.Autocorrect.Lab;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// The offline pair-model trainer: it must find the ambiguous slots (a/à), count
// the chosen variant per previous word, keep prev inside the sentence, prune
// the thin tail, and round-trip through gz TSV without loss.
[Trait("Category", "unit")]
public class PairModelTrainerTests
{
    // Minimal lexicon: only the a/à slot is ambiguous (à carries the diacritic,
    // bare "a" is a valid French word). Everything else folds to itself and is
    // a plain literal the gate owns — never an ambiguous slot.
    private const string FrenchTsv = "a\t10000\nà\t9000\n";

    private const string Corpus = "il a un chat. elle va à paris. il a faim. on va à lyon. ";

    private static (FrequencyLexicon french, AccentIndex index) Lexicon()
    {
        var french = FrequencyLexicon.LoadTsv(new StringReader(FrenchTsv));
        return (french, AccentIndex.Build(french));
    }

    private static PairModel Train(TrainerOptions? options = null)
    {
        var (french, index) = Lexicon();
        return PairModelTrainer.Train(new StringReader(Corpus), french, index, options);
    }

    [Fact]
    public void CountsBareFormPerPreviousWord()
    {
        // "a" follows "il" in two distinct sentences.
        var model = Train(new TrainerOptions { MinPairCount = 1 });

        Assert.Equal(2L, model.Bigram("a", "a", "il"));
    }

    [Fact]
    public void CountsAccentedFormPerPreviousWord()
    {
        // "à" follows "va" in two distinct sentences.
        var model = Train(new TrainerOptions { MinPairCount = 1 });

        Assert.Equal(2L, model.Bigram("a", "à", "va"));
    }

    [Fact]
    public void UnigramTotalsAggregateAcrossPrevs()
    {
        var model = Train(new TrainerOptions { MinPairCount = 1 });

        // Each variant occurs twice overall — the "" row is the per-slot total.
        Assert.Equal(2L, model.Unigram("a", "a"));
        Assert.Equal(2L, model.Unigram("a", "à"));
    }

    [Fact]
    public void PrevDoesNotCrossSentenceBoundary()
    {
        var model = Train(new TrainerOptions { MinPairCount = 1 });

        // "a" never follows "chat" or "paris": the period reset left context, so
        // no cross-sentence bigram is invented.
        Assert.Equal(0L, model.Bigram("a", "a", "chat"));
        Assert.Equal(0L, model.Bigram("a", "à", "paris"));
    }

    [Fact]
    public void OnlyAmbiguousSlotsAreCounted()
    {
        var model = Train(new TrainerOptions { MinPairCount = 1 });

        // "il", "va", "chat"… fold to themselves and are not ambiguous slots:
        // the only folded key in the model is "a".
        Assert.Equal(1, model.SlotCount);
    }

    [Fact]
    public void MinPairCountPrunesThinBigramsButKeepsUnigram()
    {
        // The shipped default floor must sit above the fabricated count-2 bigrams,
        // so they are pruned while the unigram totals survive.
        Assert.True(new TrainerOptions().MinPairCount > 2);
        var model = Train();

        Assert.Equal(0L, model.Bigram("a", "a", "il"));
        Assert.Equal(0L, model.Bigram("a", "à", "va"));
        Assert.Equal(2L, model.Unigram("a", "a"));
        Assert.Equal(2L, model.Unigram("a", "à"));
    }

    [Fact]
    public void ReportCountsSentencesTokensAndOccurrences()
    {
        var model = Train(new TrainerOptions { MinPairCount = 1 });

        // Four sentences split on the periods (the trailing blank is not one).
        Assert.Equal(4L, model.Report.Sentences);
        // 15 word tokens: il a un chat / elle va à paris / il a faim / on va à lyon.
        Assert.Equal(15L, model.Report.Tokens);
        // The a/à slot fires four times (two "a", two "à").
        Assert.Equal(4L, model.Report.AmbiguousSlotOccurrences);
    }

    [Fact]
    public void SaveLoadRoundTripsEqualRows()
    {
        var model = Train(new TrainerOptions { MinPairCount = 1 });
        string path = Path.Combine(Path.GetTempPath(), $"pairmodel-{Guid.NewGuid():N}.tsv.gz");
        try
        {
            model.SaveTsvGz(path);
            var loaded = PairModel.LoadTsvGz(path);

            var before = Sorted(model);
            var after = Sorted(loaded);
            Assert.Equal(before, after);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MaxPrevPerSlotKeepsTheHeaviestPrevs()
    {
        // Three distinct prevs for "à", with counts 5 / 4 / 3 (all clear the
        // pair floor). Cap at 2 keeps the two heaviest and drops "low".
        const string corpus = "x à. x à. x à. x à. x à. y à. y à. y à. y à. z à. z à. z à.";
        var (french, index) = Lexicon();
        var model = PairModelTrainer.Train(
            new StringReader(corpus), french, index,
            new TrainerOptions { MinPairCount = 1, MaxPrevPerSlot = 2 });

        Assert.Equal(5L, model.Bigram("a", "à", "x"));
        Assert.Equal(4L, model.Bigram("a", "à", "y"));
        Assert.Equal(0L, model.Bigram("a", "à", "z")); // the tail, pruned.
        Assert.Equal(12L, model.Unigram("a", "à"));      // the total is intact.
    }

    [Fact]
    public void CountsTrigramContextWithoutDisturbingLowerOrders()
    {
        // "à" follows "va", itself preceded by "ne", in two sentences → the
        // trigram key is "ne va". The bigram and unigram rows are untouched.
        const string corpus = "il ne va à paris. il ne va à lyon.";
        var (french, index) = Lexicon();
        var model = PairModelTrainer.Train(
            new StringReader(corpus), french, index, new TrainerOptions { MinPairCount = 1 });

        Assert.Equal(2L, model.Bigram("a", "à", "ne va")); // the trigram row
        Assert.Equal(2L, model.Bigram("a", "à", "va"));    // the bigram is still there
        Assert.Equal(2L, model.Unigram("a", "à"));         // the unigram total is intact
    }

    [Fact]
    public void MaxOrderTwoEmitsNoTrigramRows()
    {
        // Capping the trainer at order 2 reproduces the bigram model: the
        // "ne va" trigram is never counted, the bigram is unchanged.
        const string corpus = "il ne va à paris. il ne va à lyon.";
        var (french, index) = Lexicon();
        var model = PairModelTrainer.Train(
            new StringReader(corpus), french, index,
            new TrainerOptions { MinPairCount = 1, MaxOrder = 2 });

        Assert.Equal(0L, model.Bigram("a", "à", "ne va"));
        Assert.Equal(2L, model.Bigram("a", "à", "va"));
    }

    private static List<(string, string, string, long)> Sorted(PairModel model)
    {
        var rows = new List<(string, string, string, long)>();
        foreach (var r in model.Rows())
            rows.Add((r.Folded, r.Variant, r.Prev, r.Count));
        rows.Sort();
        return rows;
    }
}
