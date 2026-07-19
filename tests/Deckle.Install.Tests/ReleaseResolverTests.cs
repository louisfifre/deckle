using Deckle.Install;
using Xunit;

namespace Deckle.Install.Tests;

public sealed class ReleaseResolverTests
{
    [Fact]
    public void NativeRuntimeReleaseIsNotAnAppRelease()
    {
        var resolved = ReleaseResolver.ResolveLatestAppRelease(
        [
            Release("native-v9.0.0", "whisper-runtime.zip", "SHA256SUMS"),
            Release("v0.13.7", "Deckle-v0.13.7.zip", "Deckle-v0.13.7.zip.sha256"),
        ]);

        Assert.Equal("v0.13.7", resolved.Tag);
    }

    [Fact]
    public void HighestSemanticVersionWinsRegardlessOfApiOrder()
    {
        var resolved = ReleaseResolver.ResolveLatestAppRelease(
        [
            Release("v0.9.9", "Deckle-v0.9.9.zip", "Deckle-v0.9.9.zip.sha256"),
            Release("v0.10.0", "Deckle-v0.10.0.zip", "Deckle-v0.10.0.zip.sha256"),
        ]);

        Assert.Equal("v0.10.0", resolved.Tag);
    }

    [Fact]
    public void MissingContractAssetFailsClosed()
    {
        var release = Release("v0.13.8", "some-payload.zip", "some-payload.zip.sha256");

        var error = Assert.Throws<InvalidOperationException>(
            () => ReleaseResolver.ResolveLatestAppRelease([release]));

        Assert.Contains("Deckle-v0.13.8.zip", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.13.8")]
    [InlineData("v0.13")]
    [InlineData("v01.13.8")]
    [InlineData("release-v0.13.8")]
    public void MalformedAppTagsAreIgnored(string tag)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ReleaseResolver.ResolveLatestAppRelease(
            [
                Release(tag, $"Deckle-{tag}.zip", $"Deckle-{tag}.zip.sha256"),
            ]));

        Assert.Contains("No published Deckle app release", error.Message, StringComparison.Ordinal);
    }

    private static GitHubRelease Release(string tag, string zipName, string shaName) => new()
    {
        TagName = tag,
        Assets =
        [
            new GitHubAsset
            {
                Name = zipName,
                BrowserDownloadUrl = $"https://example.test/{zipName}",
                Size = 42,
            },
            new GitHubAsset
            {
                Name = shaName,
                BrowserDownloadUrl = $"https://example.test/{shaName}",
                Size = 64,
            },
        ],
    };
}
