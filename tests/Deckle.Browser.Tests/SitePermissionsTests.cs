using Xunit;

namespace Deckle.Browser.Tests;

// The answers kept per origin and per kind of permission.
[Trait("Category", "unit")]
public sealed class SitePermissionsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deckle-browser-perm-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public void NoStoredAnswerMeansAsk()
    {
        Assert.Null(new SitePermissions().Get("https://example.com", PermissionKind.Camera));
    }

    [Fact]
    public void AnAnswerIsStoredPerOriginAndPerKind()
    {
        var store = new SitePermissions();
        store.Set("https://example.com", PermissionKind.Camera, PermissionDecision.Allow);
        store.Set("https://example.com", PermissionKind.Microphone, PermissionDecision.Block);

        Assert.Equal(PermissionDecision.Allow, store.Get("https://example.com", PermissionKind.Camera));
        Assert.Equal(PermissionDecision.Block, store.Get("https://example.com", PermissionKind.Microphone));
        Assert.Null(store.Get("https://example.com", PermissionKind.Geolocation));
        Assert.Null(store.Get("https://other.example", PermissionKind.Camera));
    }

    [Fact]
    public void ANewAnswerReplacesTheOldOne()
    {
        var store = new SitePermissions();
        store.Set("https://example.com", PermissionKind.Camera, PermissionDecision.Allow);
        store.Set("https://example.com", PermissionKind.Camera, PermissionDecision.Block);

        Assert.Equal(PermissionDecision.Block, store.Get("https://example.com", PermissionKind.Camera));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void ARemovedAnswerAsksAgain()
    {
        var store = new SitePermissions();
        store.Set("https://example.com", PermissionKind.Camera, PermissionDecision.Allow);

        Assert.True(store.Remove("https://example.com", PermissionKind.Camera));
        Assert.False(store.Remove("https://example.com", PermissionKind.Camera));
        Assert.Null(store.Get("https://example.com", PermissionKind.Camera));
    }

    [Fact]
    public void AnOriginListsOnlyItsOwnAnswers()
    {
        var store = new SitePermissions();
        store.Set("https://example.com", PermissionKind.Microphone, PermissionDecision.Block);
        store.Set("https://example.com", PermissionKind.Camera, PermissionDecision.Allow);
        store.Set("https://other.example", PermissionKind.Camera, PermissionDecision.Allow);

        IReadOnlyList<StoredPermission> list = store.ForOrigin("https://example.com");

        Assert.Equal([PermissionKind.Camera, PermissionKind.Microphone], list.Select(p => p.Kind));
    }

    [Fact]
    public void AnswersSurviveASaveAndALoad()
    {
        string path = Path.Combine(_folder, "permissions.json");
        var store = new SitePermissions();
        store.Set("https://example.com", PermissionKind.Geolocation, PermissionDecision.Block);
        store.Set("https://example.com", PermissionKind.Notifications, PermissionDecision.Allow);
        store.Save(path);

        SitePermissions back = SitePermissions.Load(path);

        Assert.Equal(PermissionDecision.Block, back.Get("https://example.com", PermissionKind.Geolocation));
        Assert.Equal(PermissionDecision.Allow, back.Get("https://example.com", PermissionKind.Notifications));
        Assert.Equal(2, back.Count);
    }

    [Theory]
    [InlineData("not json at all {{{")]
    [InlineData("")]
    [InlineData("{\"a\":1}")]
    [InlineData("[{\"Origin\":\"https://x\",\"Kind\":\"Nonsense\",\"Decision\":\"Allow\"}]")]
    public void ACorruptFileIsAnEmptyStore(string content)
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "permissions.json");
        File.WriteAllText(path, content);

        Assert.Equal(0, SitePermissions.Load(path).Count);
    }

    [Fact]
    public void AMissingFileIsAnEmptyStore()
    {
        Assert.Equal(0, SitePermissions.Load(Path.Combine(_folder, "none.json")).Count);
    }
}
