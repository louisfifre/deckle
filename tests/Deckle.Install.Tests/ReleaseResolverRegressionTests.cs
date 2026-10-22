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
