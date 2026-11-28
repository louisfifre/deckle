using Xunit;

namespace Deckle.Browser.Tests;

[Trait("Category", "unit")]
public sealed class SessionStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deckle-browser-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void ASessionRoundTrips()
    {
        var s = new BrowserSession();
        var a = s.OpenTab("https://a.test/");
        a.Title = "A";
        var b = s.OpenTab("https://b.test/");
        b.Title = "B";
        s.OpenTab("https://c.test/");
        s.Pin(a.Id);
        s.Pin(b.Id);
        s.Select(b.Id);

        string path = Path.Combine(_folder, "session.json");
        SessionStore.Save(s, path);
        BrowserSession loaded = SessionStore.Load(path);

        Assert.Equal(["https://a.test/", "https://b.test/"], loaded.Pinned.Select(i => i.Url));
        Assert.Equal(["A", "B"], loaded.Pinned.Select(i => i.Title));
        Assert.Equal(["https://c.test/"], loaded.Tabs.Select(i => i.Url));
        Assert.All(loaded.Pinned, i => Assert.True(i.IsPinned));
        Assert.Equal(loaded.Pinned[1].Id, loaded.SelectedId);
    }

    [Fact]
    public void ASecondSaveReplacesTheFirstAndLeavesNoTempFile()
    {
        string path = Path.Combine(_folder, "session.json");
        var s = new BrowserSession();
        s.OpenTab("one");
        SessionStore.Save(s, path);
        s.OpenTab("two");
        SessionStore.Save(s, path);

        Assert.Equal(2, SessionStore.Load(path).Tabs.Count);
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    [Fact]
    public void AMissingFileIsAnEmptySession()
    {
        BrowserSession loaded = SessionStore.Load(Path.Combine(_folder, "nothing.json"));
        Assert.Empty(loaded.Pinned);
        Assert.Empty(loaded.Tabs);
        Assert.Null(loaded.SelectedId);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"Pinned\": 3}")]
    public void ACorruptFileIsAnEmptySession(string content)
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "session.json");
        File.WriteAllText(path, content);
        BrowserSession loaded = SessionStore.Load(path);
        Assert.Empty(loaded.Pinned);
        Assert.Empty(loaded.Tabs);
    }
}
