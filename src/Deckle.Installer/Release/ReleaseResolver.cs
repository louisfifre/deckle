using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deckle.Installer;

// ── ReleaseResolver ───────────────────────────────────────────────────────────
//
// Finds the release the installer should fetch. Re-running the installer means
// "get me the newest Deckle", so the target is resolved live from GitHub rather
// than baked into the stub — that decoupling is why the stub never needs a rebuild
// per release.
//
// Why the REST API and not /releases/latest: every 0.x release is published as a
// pre-release, and GitHub's "latest" endpoint deliberately skips pre-releases. The
// /releases list returns everything, newest first, so the first non-draft entry is
// the true latest during the whole 0.x phase.
//
// Asset URLs follow the frozen release convention
// (releases/download/v<X.Y.Z>/Deckle-v<X.Y.Z>.zip + .sha256); we still read them
// from the assets list when present and fall back to the convention, so a future
// naming tweak on one side doesn't silently break the other.
internal static class ReleaseResolver
{
    // The installer is distributed standalone, so owner/repo is a constant here
    // (unlike publish-app.ps1 which resolves it from the live git remote). Current
    // owner after the PelopeeNoire → louisfifre rename.
    private const string Repo = "louisfifre/deckle";

    private static readonly HttpClient s_http = CreateClient();

    // ZipSize is 0 when the asset came from the URL-convention fallback — the
    // consent recap then simply omits the download size instead of inventing one.
    public sealed record ResolvedRelease(string Tag, string ZipUrl, string Sha256Url, long ZipSize);

    public static async Task<ResolvedRelease> ResolveLatestAsync(CancellationToken ct)
    {
        string api = $"https://api.github.com/repos/{Repo}/releases";

        await using Stream stream = await s_http.GetStreamAsync(api, ct).ConfigureAwait(false);
        GitHubRelease[]? releases = await JsonSerializer
            .DeserializeAsync(stream, ReleaseJsonContext.Default.GitHubReleaseArray, ct)
            .ConfigureAwait(false);

        GitHubRelease? latest = releases?.FirstOrDefault(r => !r.Draft && r.TagName is not null);
        if (latest?.TagName is not { } tag)
            throw new InvalidOperationException("No published release found on GitHub.");

        GitHubAsset? zipAsset = FindAsset(latest, name => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        GitHubAsset? shaAsset = FindAsset(latest, name => name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase));

        string zip = zipAsset?.BrowserDownloadUrl
            ?? $"https://github.com/{Repo}/releases/download/{tag}/Deckle-{tag}.zip";
        string sha = shaAsset?.BrowserDownloadUrl ?? zip + ".sha256";

        return new ResolvedRelease(tag, zip, sha, zipAsset?.Size ?? 0);
    }

    private static GitHubAsset? FindAsset(GitHubRelease release, Func<string, bool> match) =>
        release.Assets?.FirstOrDefault(a => a.Name is not null && match(a.Name));

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
