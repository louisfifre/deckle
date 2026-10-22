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
