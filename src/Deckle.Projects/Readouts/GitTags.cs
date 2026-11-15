using System.Text.RegularExpressions;

namespace Deckle.Projects;

// The version a project's Git tags say it is at: the highest tag that reads as
// a release number, compared as numbers.
//
// A tag counts when it is "v0.131.0" or "0.32.0": three numbers, with or
// without the leading v. A pre-release suffix ("v1.0.0-rc.1"), a word
// ("latest") or a date-like tag is ignored, not parsed. The comparison is
// numeric because a text sort puts 0.9.0 above 0.131.0.
//
// The highest of all tags, not the highest reachable from the checked-out
// commit (what Deckle's own build reads): on a dashboard the person wants the
// latest release, wherever the checkout stands.
public static partial class GitTags
{
    // The tag shown, always with one leading "v", or null when no tag is a
    // release number.
    public static string? Highest(IEnumerable<string> tags)
    {
        (long Major, long Minor, long Patch)? best = null;
        foreach (string tag in tags)
        {
            Match match = Release().Match(tag.Trim());
            if (!match.Success) continue;
            if (!long.TryParse(match.Groups[1].Value, out long major)
                || !long.TryParse(match.Groups[2].Value, out long minor)
                || !long.TryParse(match.Groups[3].Value, out long patch))
                continue;

            var candidate = (major, minor, patch);
            if (best is null || candidate.CompareTo(best.Value) > 0) best = candidate;
        }
        return best is { } v ? $"v{v.Major}.{v.Minor}.{v.Patch}" : null;
    }

    // "v0.32.0" and "0.32.0" are the same version: the form compared and listed
    // is the number alone.
    public static string WithoutV(string version) =>
        version.Length > 1 && (version[0] is 'v' or 'V') && char.IsAsciiDigit(version[1]) ? version[1..] : version;

    [GeneratedRegex(@"^[vV]?(\d+)\.(\d+)\.(\d+)$")]
    private static partial Regex Release();
}
