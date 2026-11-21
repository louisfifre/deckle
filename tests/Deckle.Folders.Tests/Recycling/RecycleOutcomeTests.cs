using Xunit;

namespace Deckle.Folders.Tests;

// What a list does with a row once the bin has answered: an item no longer in
// its folder leaves the list, one still there keeps its row.
[Trait("Category", "unit")]
public sealed class RecycleOutcomeTests
{
    [Theory]
    [InlineData(RecycleStatus.Recycled, true)]
    [InlineData(RecycleStatus.Missing, true)]
    [InlineData(RecycleStatus.DeletedForGood, true)]
    [InlineData(RecycleStatus.Refused, false)]
    [InlineData(RecycleStatus.Failed, false)]
    [InlineData(RecycleStatus.NotDone, false)]
    public void AnItemLeavesTheListOnlyWhenItIsNoLongerInItsFolder(RecycleStatus status, bool left)
    {
        Assert.Equal(left, new RecycleOutcome(status).LeftItsFolder);
    }

    [Fact]
    public void OneItemGetsTheOneOutcomeTheBinGaveIt()
    {
        var bin = new AnsweringBin(new RecycleOutcome(RecycleStatus.Refused, 5));

        RecycleOutcome outcome = bin.RecycleItem(@"C:\nowhere\a.txt");

        Assert.Equal(RecycleStatus.Refused, outcome.Status);
        Assert.Equal(5, outcome.HResult);
        Assert.Equal([@"C:\nowhere\a.txt"], bin.Asked);
    }

    private sealed class AnsweringBin(RecycleOutcome answer) : IRecycleBin
    {
        public List<string> Asked { get; } = [];

        public void Recycle(IReadOnlyList<string> paths, Action<int, RecycleOutcome> onEach, CancellationToken cancel)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                Asked.Add(paths[i]);
                onEach(i, answer);
            }
        }
    }
}
