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

    [Fact]
    public void ABatchCollidingWithAnEarlierIdRegistersNoneOfItsMembers()
    {
        // Cross-batch collision: the batch validates whole before mutating, so
        // the otherwise-good member must not slip in alongside the duplicate.
        var catalog = new NotificationCatalog();
        catalog.Register(new[] { Descriptors.Make("playground.existing") });

        Assert.Throws<InvalidOperationException>(() => catalog.Register(new[]
        {
            Descriptors.Make("playground.good"),
            Descriptors.Make("playground.existing"),
        }));

        Assert.False(catalog.IsRegistered("playground.good"));
    }

    [Fact]
    public void ABatchWithAnInternalDuplicateRegistersNoneOfItsMembers()
    {
        // Within-batch duplicate: same atomicity guarantee — the leading
        // good member is rolled back with the rest.
        var catalog = new NotificationCatalog();

        Assert.Throws<InvalidOperationException>(() => catalog.Register(new[]
        {
            Descriptors.Make("playground.good2"),
            Descriptors.Make("playground.dup"),
            Descriptors.Make("playground.dup"),
        }));

        Assert.False(catalog.IsRegistered("playground.good2"));
    }

    [Fact]
    public void ANullDescriptorInTheBatchThrowsInvalidOperationException()
    {
        var catalog = new NotificationCatalog();

        Assert.Throws<InvalidOperationException>(() => catalog.Register(
            new NotificationDescriptor[] { Descriptors.Make("playground.ok"), null! }));
    }

    [Fact]
    public void AWhitespaceIdThrowsInvalidOperationException()
    {
        var catalog = new NotificationCatalog();

        Assert.Throws<InvalidOperationException>(() =>
            catalog.Register(new[] { Descriptors.Make("   ") }));
    }
}
