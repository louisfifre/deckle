using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// The Entertainment area picker offers the bridge's Entertainment areas and
// nothing else (decided 2026-11-25): an ordinary room or zone cannot be
// streamed to, so offering one would only lead to a start that fails.
[Trait("Category", "unit")]
public sealed class HueGroupSelectionTests
{
    [Fact]
    public void ThePickerOffersEntertainmentAreasOnlyInTheBridgesOrder()
    {
        IReadOnlyList<HueGroup> groups =
        [
            new("1", "Living room", "Room", 4),
            new("5", "TV", "Entertainment", 3),
            new("6", "Downstairs", "Zone", 7),
            new("9", "Desk", "Entertainment", 2),
            new("11", "Hallway", "LightGroup", 1),
        ];

        var offered = HueGroupSelection.EntertainmentAreas(groups);

        Assert.Equal(["5", "9"], offered.Select(g => g.Id));
    }

    [Fact]
    public void ABridgeWithNoEntertainmentAreaOffersNothing()
    {
        IReadOnlyList<HueGroup> groups =
        [
            new("1", "Living room", "Room", 4),
            new("6", "Downstairs", "Zone", 7),
        ];

        Assert.Empty(HueGroupSelection.EntertainmentAreas(groups));
    }

    [Fact]
    public void ARoomChosenBeforeIsNotSelectedAmongTheAreas()
    {
        IReadOnlyList<HueGroup> groups =
        [
            new("1", "Living room", "Room", 4),
            new("5", "TV", "Entertainment", 3),
        ];

        var offered = HueGroupSelection.EntertainmentAreas(groups);

        Assert.Equal(-1, HueGroupSelection.IndexOf(offered.Select(g => g.Id).ToArray(), "1"));
    }
}
