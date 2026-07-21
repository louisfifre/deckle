using System.Collections.Specialized;
using Deckle.Diagnostics.Logging.Ui.Collections;
using Xunit;

namespace Deckle.Diagnostics.Logging.Tests;

[Trait("Category", "unit")]
public sealed class RangeObservableCollectionTests
{
    [Fact]
    public void ReplaceAllPublishesOneResetWithTheCompleteNewProjection()
    {
        var collection = new RangeObservableCollection<int> { 1, 2 };
        var changes = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => changes.Add(args);

        collection.ReplaceAll([3, 4, 5]);

        Assert.Equal([3, 4, 5], collection);
        NotifyCollectionChangedEventArgs change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Reset, change.Action);
    }
}
