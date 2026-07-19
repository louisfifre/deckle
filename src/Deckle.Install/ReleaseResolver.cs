using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deckle.Install;

// ── ReleaseResolver ───────────────────────────────────────────────────────────
//
// Finds the newest published Deckle release on GitHub. Shared by the two
// consumers of the release convention: the download stub ("get me the newest
// Deckle" — the decoupling that spares the stub a rebuild per release) and the
// installed app's update check (compare the newest tag against the registered
// version). Lives here, below both, under this module's dependency-free and
// AOT-safe contract — HttpClient and source-generated JSON only.
//
// Why the REST API and not /releases/latest: every 0.x release is published as a
// pre-release, and GitHub's "latest" endpoint deliberately skips pre-releases. The
// /releases list also contains the native runtime's independent `native-v*`
// releases. App releases are therefore selected by their strict vX.Y.Z tag and
// compared semantically instead of trusting API order.
//
// Asset names are part of the frozen release contract. A published app release
// missing either exact asset is rejected: synthesizing a plausible URL would
// hide a partial release precisely when the installer must fail closed.
public static class ReleaseResolver
{
    // The stub is distributed standalone, so owner/repo is a constant here
    // (unlike publish-app.ps1 which resolves it from the live git remote). Current
    // owner after the PelopeeNoire → louisfifre rename.
    private const string Repo = "louisfifre/deckle";

    private static readonly HttpClient s_http = CreateClient();

    // Size is read from the exact ZIP asset and feeds the consent recap.
    public sealed record ResolvedRelease(string Tag, string ZipUrl, string Sha256Url, long ZipSize);

    public static async Task<ResolvedRelease> ResolveLatestAsync(CancellationToken ct)
    {
        string api = $"https://api.github.com/repos/{Repo}/releases";

        await using Stream stream = await s_http.GetStreamAsync(api, ct).ConfigureAwait(false);
        GitHubRelease[]? releases = await JsonSerializer
            .DeserializeAsync(stream, ReleaseJsonContext.Default.GitHubReleaseArray, ct)
            .ConfigureAwait(false);

        return ResolveLatestAppRelease(releases ?? []);
    }

    // Fetches and parses the payload's .sha256 sidecar — `<hex> *<filename>`
    // (sha256sum -c format), lower-cased hex returned.
    public static async Task<string> GetSha256Async(ResolvedRelease release, CancellationToken ct)
    {
        string content = await s_http.GetStringAsync(release.Sha256Url, ct).ConfigureAwait(false);
        return content.Trim().Split(' ', '\t', '\n', '\r')[0].ToLowerInvariant();
    }

    // "v0.7.1" → "0.7.1" — the tag with its leading v dropped, the form the
    // Installed-apps entry stores and version comparisons parse.
    public static string BareVersion(string tag) => tag.StartsWith('v') ? tag[1..] : tag;

    internal static ResolvedRelease ResolveLatestAppRelease(IEnumerable<GitHubRelease> releases)
    {
        GitHubRelease? latest = releases
            .Where(r => !r.Draft && TryParseAppVersion(r.TagName, out _))
            .OrderByDescending(r => ParseAppVersion(r.TagName!))
            .FirstOrDefault();

        if (latest?.TagName is not { } tag)
            throw new InvalidOperationException("No published Deckle app release found on GitHub.");

        string zipName = $"Deckle-{tag}.zip";
        string shaName = $"{zipName}.sha256";
        GitHubAsset zipAsset = FindRequiredAsset(latest, zipName);
        GitHubAsset shaAsset = FindRequiredAsset(latest, shaName);

        return new ResolvedRelease(
            tag,
            RequireAssetUrl(zipAsset, zipName),
            RequireAssetUrl(shaAsset, shaName),
            zipAsset.Size);
    }

    private static bool TryParseAppVersion(string? tag, out Version version)
    {
        version = new Version();
        if (tag is null || tag.Length < 2 || tag[0] != 'v') return false;
        string bare = tag[1..];
        if (!Version.TryParse(bare, out Version? parsed) || parsed is null) return false;
        if (parsed.Major < 0 || parsed.Minor < 0 || parsed.Build < 0 || parsed.Revision >= 0)
            return false;

        // Version.TryParse accepts forms such as 01.2.3; the release contract
        // does not. Round-tripping enforces the canonical vX.Y.Z spelling.
        if (!string.Equals(bare, parsed.ToString(3), StringComparison.Ordinal)) return false;
        version = parsed;
        return true;
    }

    private static Version ParseAppVersion(string tag)
    {
        _ = TryParseAppVersion(tag, out Version version);
        return version;
    }

    private static GitHubAsset FindRequiredAsset(GitHubRelease release, string expectedName) =>
        release.Assets?.SingleOrDefault(a =>
            string.Equals(a.Name, expectedName, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"Release {release.TagName} is missing required asset {expectedName}.");

    private static string RequireAssetUrl(GitHubAsset asset, string name) =>
        !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl)
            ? asset.BrowserDownloadUrl
            : throw new InvalidOperationException($"Release asset {name} has no download URL.");

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        // GitHub's API rejects requests without a User-Agent (HTTP 403).
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Deckle-Installer");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

// Minimal projection of the GitHub release JSON — only the fields the resolver
// reads. snake_case maps tag_name / browser_download_url without per-property
// attributes.
internal sealed class GitHubRelease
{
    public string? TagName { get; set; }
    public bool Draft { get; set; }
    public bool Prerelease { get; set; }
    public GitHubAsset[]? Assets { get; set; }
}

internal sealed class GitHubAsset
{
    public string? Name { get; set; }
    public string? BrowserDownloadUrl { get; set; }
    public long Size { get; set; } // bytes — feeds the consent recap's download size
}

// Source-generated (de)serialization — the AOT-safe path. Reflection-based
// JsonSerializer is not trim/AOT-safe; the context wires concrete metadata at
// build time instead.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(GitHubRelease[]))]
internal partial class ReleaseJsonContext : JsonSerializerContext;
