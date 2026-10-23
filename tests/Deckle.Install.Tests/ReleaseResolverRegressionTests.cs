using Deckle.Install;
using Xunit;

namespace Deckle.Install.Tests;

[Trait("Category", "regression")]
public sealed class ReleaseResolverRegressionTests
{
    [Fact]
    public void A_prerelease_never_wins_over_a_published_release()
    {
        var resolved = ReleaseResolver.ResolveLatestAppRelease(
        [
            Release("v9.0.0", prerelease: true),
            Release("v0.13.7", prerelease: false),
        ]);

        Assert.Equal("v0.13.7", resolved.Tag);
    }

    [Fact]
    public void Only_a_prerelease_means_there_is_no_release_to_install()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ReleaseResolver.ResolveLatestAppRelease([Release("v9.0.0", prerelease: true)]));

        Assert.Contains("No published Deckle app release", error.Message, StringComparison.Ordinal);
    }

    // A release that lists the same required asset twice is not the frozen
    // contract, so the resolver refuses it. The refusal has to name the release
    // and the asset: the bare LINQ message ("Sequence contains more than one
    // matching element") says neither.
    [Fact]
    public void A_release_listing_a_required_asset_twice_is_refused_by_naming_it()
    {
        var release = new GitHubRelease
        {
            TagName = "v0.13.9",
            Assets =
            [
                new GitHubAsset { Name = "Deckle-v0.13.9.zip", BrowserDownloadUrl = "https://example.test/a.zip", Size = 42 },
                new GitHubAsset { Name = "Deckle-v0.13.9.zip", BrowserDownloadUrl = "https://example.test/b.zip", Size = 43 },
                new GitHubAsset { Name = "Deckle-v0.13.9.zip.sha256", BrowserDownloadUrl = "https://example.test/a.sha256", Size = 64 },
            ],
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => ReleaseResolver.ResolveLatestAppRelease([release]));

        Assert.Contains("v0.13.9", error.Message, StringComparison.Ordinal);
        Assert.Contains("Deckle-v0.13.9.zip", error.Message, StringComparison.Ordinal);
    }

    // The listing mixes the app's releases with the native runtime's `native-v*`
    // ones, newest first, and GitHub serves it by pages. When enough native
    // releases are newer than the newest app release, that app release sits on a
    // later page: reading page one alone finds none and the installer refuses.
    [Fact]
    public async Task The_newest_app_release_is_found_on_a_later_page()
    {
        var pages = new Dictionary<int, GitHubRelease[]>
        {
            [1] = [.. Enumerable.Range(0, ReleaseResolver.PageSize).Select(i => Release($"native-v1.{i}.0", prerelease: false))],
            [2] = [Release("v0.13.7", prerelease: false), Release("v0.13.6", prerelease: false)],
        };

        var resolved = await ReleaseResolver.ResolveLatestAsync(
            (page, _) => Task.FromResult(pages.GetValueOrDefault(page, [])), CancellationToken.None);

        Assert.Equal("v0.13.7", resolved.Tag);
    }

    // The page that holds an app release ends the search: the pages after it are
    // never asked for.
    [Fact]
    public async Task The_search_stops_at_the_first_page_holding_an_app_release()
    {
        var asked = new List<int>();
        var pages = new Dictionary<int, GitHubRelease[]>
        {
            [1] = [.. Enumerable.Range(0, ReleaseResolver.PageSize).Select(i => Release($"native-v1.{i}.0", prerelease: false))],
            [2] = [Release("v0.13.7", prerelease: false)],
            [3] = [Release("v0.13.6", prerelease: false)],
        };

        await ReleaseResolver.ResolveLatestAsync(
            (page, _) => { asked.Add(page); return Task.FromResult(pages.GetValueOrDefault(page, [])); },
            CancellationToken.None);

        Assert.Equal([1, 2], asked);
    }

    // A listing that holds no app release at all ends on a short page, with the
    // same refusal as before.
    [Fact]
    public async Task A_listing_without_any_app_release_is_refused_once_it_runs_out()
    {
        var asked = new List<int>();
        var pages = new Dictionary<int, GitHubRelease[]>
        {
            [1] = [.. Enumerable.Range(0, ReleaseResolver.PageSize).Select(i => Release($"native-v1.{i}.0", prerelease: false))],
            [2] = [Release("native-v0.9.0", prerelease: false)],
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ReleaseResolver.ResolveLatestAsync(
            (page, _) => { asked.Add(page); return Task.FromResult(pages.GetValueOrDefault(page, [])); },
            CancellationToken.None));

        Assert.Contains("No published Deckle app release", error.Message, StringComparison.Ordinal);
        Assert.Equal([1, 2], asked);
    }

    // The loop has a ceiling: a listing that only ever returns full pages of
    // native releases cannot keep the installer asking for ever.
    [Fact]
    public async Task The_search_gives_up_at_the_page_cap()
    {
        int asked = 0;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ReleaseResolver.ResolveLatestAsync(
            (_, _) =>
            {
                asked++;
                return Task.FromResult(Enumerable.Range(0, ReleaseResolver.PageSize)
                    .Select(i => Release($"native-v1.{i}.0", prerelease: false)).ToArray());
            },
            CancellationToken.None));

        Assert.Equal(ReleaseResolver.MaxPages, asked);
        Assert.Contains("No published Deckle app release", error.Message, StringComparison.Ordinal);
    }

    private static GitHubRelease Release(string tag, bool prerelease) => new()
    {
        TagName = tag,
        Prerelease = prerelease,
        Assets =
        [
            new GitHubAsset
            {
                Name = $"Deckle-{tag}.zip",
                BrowserDownloadUrl = $"https://example.test/Deckle-{tag}.zip",
                Size = 42,
            },
            new GitHubAsset
            {
                Name = $"Deckle-{tag}.zip.sha256",
                BrowserDownloadUrl = $"https://example.test/Deckle-{tag}.zip.sha256",
                Size = 64,
            },
        ],
    };
}
