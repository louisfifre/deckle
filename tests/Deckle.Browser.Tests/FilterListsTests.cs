using Xunit;

namespace Deckle.Browser.Tests;

// The filter sources and when a kept copy is too old.
[Trait("Category", "unit")]
public sealed class FilterListsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ACopyOfFourDaysExactlyIsNotStale() => Assert.False(FilterLists.IsStale(Now.AddDays(-4), Now));

    [Fact]
    public void ACopyOlderThanFourDaysIsStale() => Assert.True(FilterLists.IsStale(Now.AddDays(-4).AddSeconds(-1), Now));

    [Fact]
    public void AFreshCopyIsNotStale() => Assert.False(FilterLists.IsStale(Now.AddHours(-1), Now));

    [Fact]
    public void ACopyDatedInTheFutureIsNotStale() => Assert.False(FilterLists.IsStale(Now.AddDays(2), Now));

    [Fact]
    public void TheFiveListsAndTheResourcesAreNamed()
    {
        Assert.Equal(
            ["EasyList", "EasyPrivacy", "uBlock filters", "uBlock quick fixes", "uBlock privacy"],
            FilterLists.Lists.Select(l => l.Name));
        Assert.Equal(6, FilterLists.All.Count());
    }

    [Fact]
    public void EverySourceIsHttpsAndHasItsOwnFileName()
    {
        Assert.All(FilterLists.All, s => Assert.Equal("https", s.Address.Scheme));
        Assert.Equal(6, FilterLists.All.Select(s => s.FileName).Distinct().Count());
    }

    [Fact]
    public void TheAddressesAreTheOnesTheListsArePublishedAt()
    {
        Assert.Equal("https://easylist.to/easylist/easylist.txt", FilterLists.Lists[0].Address.ToString());
        Assert.Equal("https://raw.githubusercontent.com/brave/adblock-resources/master/dist/resources.json", FilterLists.Resources.Address.ToString());
    }

    [Fact]
    public void OnlyUBlocksOwnListsAreTrusted()
    {
        Assert.Equal(
            ["uBlock filters", "uBlock quick fixes", "uBlock privacy"],
            FilterLists.Lists.Where(l => l.Trusted).Select(l => l.Name));
    }

    [Fact]
    public void TheScriptletModulesAreFetchedAtThePinnedTagAndIncludeTheEntry()
    {
        Assert.Contains(FilterLists.UboEntry, FilterLists.UboFiles);
        Assert.Equal(FilterLists.UboFiles.Count, FilterLists.UboFiles.Distinct().Count());
        Assert.Equal(
            $"https://raw.githubusercontent.com/gorhill/uBlock/{FilterLists.UboTag}/src/js/resources/scriptlets.js",
            FilterLists.UboAddress(FilterLists.UboEntry).ToString());
        Assert.Contains(FilterLists.UboTag, FilterLists.UboConvertedFileName());
    }
}
