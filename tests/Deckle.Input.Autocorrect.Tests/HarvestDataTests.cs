using Deckle.Input.Autocorrect.Cli;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// The harvest's aggregation: a repeated signal collapses onto one entry whose
// count grows and whose last-seen advances, while distinct signals stay apart.
// Time is passed in, so the behaviour is asserted exactly, not observed.
public sealed class HarvestDataTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(5);

    [Fact]
    public void AFirstEditPairIsRecordedOnceWithBothStampsAtNow()
    {
        var data = new HarvestData();

        data.RecordEdit("captes", "capte", T0);

        var edit = Assert.Single(data.Edits);
        Assert.Equal("captes", edit.Original);
        Assert.Equal("capte", edit.Replacement);
        Assert.Equal(1, edit.Count);
        Assert.Equal(T0, edit.FirstSeenUtc);
        Assert.Equal(T0, edit.LastSeenUtc);
    }

    [Fact]
    public void TheSameEditPairAggregatesItsCountAndAdvancesLastSeenOnly()
    {
        var data = new HarvestData();

        data.RecordEdit("captes", "capte", T0);
        data.RecordEdit("captes", "capte", T1);

        var edit = Assert.Single(data.Edits);
        Assert.Equal(2, edit.Count);
        Assert.Equal(T0, edit.FirstSeenUtc); // first-seen is sticky
        Assert.Equal(T1, edit.LastSeenUtc);  // last-seen tracks the latest
    }

    [Fact]
    public void DifferentReplacementsOfTheSameWordAreDistinctPairs()
    {
        var data = new HarvestData();

        data.RecordEdit("ca", "ça", T0);
        data.RecordEdit("ca", "ca", T0); // the pair key is (original, replacement)

        Assert.Equal(2, data.Edits.Count);
    }

    [Fact]
    public void AnUnknownWordAggregatesByItsSurfaceForm()
    {
        var data = new HarvestData();

        data.RecordUnknownWord("renommes", T0);
        data.RecordUnknownWord("renommes", T1);
        data.RecordUnknownWord("captes", T1);

        Assert.Equal(2, data.UnknownWords.Count);
        var renommes = data.UnknownWords.Single(w => w.Word == "renommes");
        Assert.Equal(2, renommes.Count);
        Assert.Equal(T0, renommes.FirstSeenUtc);
        Assert.Equal(T1, renommes.LastSeenUtc);
    }
}
