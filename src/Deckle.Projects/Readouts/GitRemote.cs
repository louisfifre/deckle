using System.Text.RegularExpressions;

namespace Deckle.Projects;

// The remote of a project, as the page links to it: the host's name ("GitHub")
// and the address of the repository's web page.
//
// A web page is built only for the shapes that name a host and a repository
// path: https://host/owner/repo(.git), git@host:owner/repo(.git) and
// ssh://git@host/owner/repo. The page is always https, whatever the remote
// speaks. An http:// or git:// remote, a file:// remote and a local path give
// none, and the button is not shown (spec: "only when the project's remote has
// one"). A user name or token written in the address is dropped, never kept.
public sealed partial record GitRemote(string Host, string HostName, Uri WebPage)
{
    private static readonly Dictionary<string, string> KnownHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["github.com"] = "GitHub",
        ["gitlab.com"] = "GitLab",
        ["codeberg.org"] = "Codeberg",
        ["bitbucket.org"] = "Bitbucket",
    };

    // The URL git holds for the remote to follow: origin, else the first one.
    // The text is `git config --get-regexp "^remote\..+\.url$"`: one line per
    // remote, "remote.<name>.url <address>".
    public static string? PickUrl(string configOutput)
    {
        var remotes = new List<(string Name, string Url)>();
        foreach (string raw in configOutput.Split('\n'))
        {
            string line = raw.Trim();
            int space = line.IndexOf(' ');
            if (space <= 0 || !line.StartsWith("remote.", StringComparison.Ordinal)) continue;
            string key = line[..space];
            if (!key.EndsWith(".url", StringComparison.Ordinal)) continue;
            string name = key["remote.".Length..^".url".Length];
            string url = line[(space + 1)..].Trim();
            if (name.Length > 0 && url.Length > 0) remotes.Add((name, url));
        }
        if (remotes.Count == 0) return null;
        return remotes.FirstOrDefault(r => r.Name == "origin").Url ?? remotes[0].Url;
    }

    // The remote a web page can be built for, or null.
    public static GitRemote? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string text = url.Trim();

        string? host;
        string? path;
        if (text.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)) return null;
            // ssh and https name a repository; http, git and file do not give
            // a page worth linking to.
            if (uri.Scheme is not ("https" or "ssh")) return null;
            host = uri.Host;
            path = Uri.UnescapeDataString(uri.AbsolutePath);
        }
        else
        {
            Match scp = ScpLike().Match(text);
            if (!scp.Success) return null;
            host = scp.Groups["host"].Value;
            path = scp.Groups["path"].Value;
        }

        // A drive letter ("C:\repo") reads like a host in the scp shape.
        if (string.IsNullOrEmpty(host) || host.Length < 2) return null;

        string[] segments = [.. path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)];
        if (segments.Length < 2) return null;
        if (segments[^1].EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            segments[^1] = segments[^1][..^".git".Length];
        if (segments.Any(s => s.Length == 0)) return null;

        if (!Uri.TryCreate($"https://{host}/{string.Join('/', segments.Select(Uri.EscapeDataString))}", UriKind.Absolute, out Uri? page))
            return null;

        string bare = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        string name = KnownHosts.TryGetValue(bare, out string? known) ? known : host;
        return new GitRemote(host, name, page);
    }

    [GeneratedRegex(@"^(?:[^@/\s]+@)?(?<host>[^:/\s]+):(?!//)(?<path>.+)$")]
    private static partial Regex ScpLike();
}
