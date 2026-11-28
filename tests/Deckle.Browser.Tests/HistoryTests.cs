using Xunit;

namespace Deckle.Browser.Tests;

[Trait("Category", "unit")]
public sealed class HistoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deckle-browser-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Theory]
    [InlineData("https://example.test/a", true)]
    [InlineData("http://example.test/a", true)]
    [InlineData("about:blank", false)]
    [InlineData("file:///C:/Code/x.html", false)]
    [InlineData("edge://settings", false)]
    [InlineData("", false)]
    public void OnlyHttpAndHttpsPagesAreRecordable(string url, bool recordable) =>
        Assert.Equal(recordable, History.IsRecordable(url));

    [Fact]
    public void ARevisitCountsAndKeepsTheTitleWhenTheNewOneIsEmpty()
    {
        var h = new History();
        h.Record("https://example.test/a", "Alpha", T0);
        h.Record("https://example.test/a", "", T0.AddHours(1));

        HistoryEntry entry = Assert.Single(h.Entries);
        Assert.Equal(2, entry.Visits);
        Assert.Equal("Alpha", entry.Title);
        Assert.Equal(T0.AddHours(1), entry.LastVisit);
    }

    [Fact]
    public void ARecordOfAPageThatIsNotRecordableChangesNothing()
    {
        var h = new History();
        Assert.False(h.Record("about:blank", "x", T0));
        Assert.Equal(0, h.Count);
    }

    [Fact]
    public void ARetitleChangesTheTitleWithoutCountingAVisit()
    {
        var h = new History();
        h.Record("https://example.test/a", "", T0);
        h.Retitle("https://example.test/a", "Alpha");

        Assert.Equal("Alpha", Assert.Single(h.Entries).Title);
        Assert.Equal(1, Assert.Single(h.Entries).Visits);
    }

    [Fact]
    public void TheOldestEntriesGoBeyondTheCapacity()
    {
        var h = new History();
        for (int i = 0; i < History.Capacity + 3; i++) h.Record($"https://example.test/{i}", "", T0.AddSeconds(i));

        Assert.Equal(History.Capacity, h.Count);
        Assert.DoesNotContain(h.Entries, e => e.Url.EndsWith("/0") || e.Url.EndsWith("/1") || e.Url.EndsWith("/2"));
        Assert.Contains(h.Entries, e => e.Url.EndsWith("/3"));
    }

    [Fact]
    public void NothingTypedSuggestsNothing()
    {
        var h = new History();
        h.Record("https://example.test/a", "Alpha", T0);
        Assert.Empty(h.Suggest(""));
        Assert.Empty(h.Suggest("   "));
    }

    [Fact]
    public void TheHostStartingWithTheTextMatchesAndAWwwIsIgnoredOnBothSides()
    {
        var h = new History();
        h.Record("https://www.example.test/page", "Nothing", T0);
        h.Record("https://other.test/page", "Nothing", T0);

        Assert.Equal(["https://www.example.test/page"], h.Suggest("exam").Select(s => s.Url));
        Assert.Equal(["https://www.example.test/page"], h.Suggest("www.exam").Select(s => s.Url));
    }

    [Fact]
    public void TheAddressOrTheTitleContainingTheTextMatchesWhateverTheCase()
    {
        var h = new History();
        h.Record("https://a.test/Recipes/Soup", "x", T0);
        h.Record("https://b.test/", "A Fine PAGE", T0);
        h.Record("https://c.test/", "unrelated", T0);

        Assert.Equal(["https://a.test/Recipes/Soup"], h.Suggest("recipes").Select(s => s.Url));
        Assert.Equal(["https://b.test/"], h.Suggest("fine page").Select(s => s.Url));
    }

    [Fact]
    public void SuggestionsAreRankedByVisitsThenRecencyAndCappedAtMax()
    {
        var h = new History();
        h.Record("https://few.test/page", "page", T0.AddHours(5));
        h.Record("https://many.test/page", "page", T0);
        h.Record("https://many.test/page", "page", T0);
        h.Record("https://recent.test/page", "page", T0.AddHours(9));

        Assert.Equal(
            ["https://many.test/page", "https://recent.test/page", "https://few.test/page"],
            h.Suggest("page").Select(s => s.Url));
        Assert.Equal(2, h.Suggest("page", 2).Count);
    }

    [Fact]
    public void ASuggestionCarriesTheTitleAndTheRegistrableDomain()
    {
        var h = new History();
        h.Record("https://news.example.test/story", "A story", T0);

        Suggestion s = Assert.Single(h.Suggest("story"));
        Assert.Equal("A story", s.Title);
        Assert.Equal("example.test", s.Domain);
    }

    [Fact]
    public void AHistoryRoundTripsThroughItsFile()
    {
        var h = new History();
        h.Record("https://example.test/a", "Alpha", T0);
        h.Record("https://example.test/a", "Alpha", T0.AddMinutes(1));
        string path = Path.Combine(_folder, "history.json");

        HistoryStore.Save(h, path);
        History loaded = HistoryStore.Load(path);

        HistoryEntry entry = Assert.Single(loaded.Entries);
        Assert.Equal(new HistoryEntry("https://example.test/a", "Alpha", T0.AddMinutes(1), 2), entry);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void AMissingOrCorruptFileIsAnEmptyHistory()
    {
        Assert.Equal(0, HistoryStore.Load(Path.Combine(_folder, "none.json")).Count);

        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "bad.json");
        File.WriteAllText(path, "{ not json");
        Assert.Equal(0, HistoryStore.Load(path).Count);
    }
}
