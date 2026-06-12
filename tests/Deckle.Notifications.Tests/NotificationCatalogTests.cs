using System;
using System.Linq;
using Deckle.Notifications;
using Xunit;

namespace Deckle.Notifications.Tests;

// Unit: the catalogue is a boot-time index keyed by descriptor Id. It is the
// fail-fast guard that a duplicate Id never silently overwrites another, and
// the source of truth the dispatcher consults before routing.
[Trait("Category", "unit")]
public class NotificationCatalogTests
{
    [Fact]
    public void RegisteringTwoDescriptorsWithTheSameIdThrows()
    {
        var catalog = new NotificationCatalog();

        Assert.Throws<InvalidOperationException>(() => catalog.Register(new[]
        {
            Descriptors.Make("playground.dup"),
            Descriptors.Make("playground.dup"),
        }));
    }

    [Fact]
    public void RegisteringAnIdAlreadyPresentInAnEarlierBatchThrows()
    {
        var catalog = new NotificationCatalog();
        catalog.Register(new[] { Descriptors.Make("playground.first") });

        Assert.Throws<InvalidOperationException>(() =>
            catalog.Register(new[] { Descriptors.Make("playground.first") }));
    }

    [Fact]
    public void IsRegisteredReportsTrueForAKnownIdAndFalseOtherwise()
    {
        var catalog = new NotificationCatalog();
        catalog.Register(new[] { Descriptors.Make("playground.known") });

        Assert.True(catalog.IsRegistered("playground.known"));
        Assert.False(catalog.IsRegistered("playground.unknown"));
    }

    [Fact]
    public void AllExposesEveryRegisteredDescriptor()
    {
        var catalog = new NotificationCatalog();
        catalog.Register(new[]
        {
            Descriptors.Make("playground.one"),
            Descriptors.Make("playground.two"),
        });

        var ids = catalog.All.Select(d => d.Id).ToHashSet();
        Assert.Equal(new[] { "playground.one", "playground.two" }.ToHashSet(), ids);
    }
}
