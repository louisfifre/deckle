using System.Collections.Specialized;
using Deckle.Diagnostics.Logging.Ui.Collections;
using Xunit;

namespace Deckle.Diagnostics.Logging.Tests;

[Trait("Category", "regression")]
public sealed class RangeObservableCollectionRegressionTests
{
    [Fact]
    public void A_replacement_read_lazily_from_the_collection_itself_keeps_its_rows()
    {
        var collection = new RangeObservableCollection<int> { 1, 2, 3, 4 };

        // A filtered projection of the current rows: it reads the collection
        // while it is being replaced.
        collection.ReplaceAll(collection.Where(n => n % 2 == 0));

        Assert.Equal([2, 4], collection);
    }

    [Fact]
    public void A_replacement_that_throws_midway_leaves_the_collection_untouched_and_silent()
    {
        var collection = new RangeObservableCollection<int> { 1, 2 };
        var changes = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => changes.Add(args);

        Assert.Throws<InvalidOperationException>(() => collection.ReplaceAll(Faulty()));

        Assert.Equal([1, 2], collection);
        Assert.Empty(changes);

        static IEnumerable<int> Faulty()
        {
            yield return 9;
            throw new InvalidOperationException("the source failed");
        }
    }
}
