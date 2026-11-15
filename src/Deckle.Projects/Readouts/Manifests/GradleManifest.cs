using System.Text.RegularExpressions;

namespace Deckle.Projects;

// The two ways a Gradle build says its version: `mod_version=1.2.3` in
// gradle.properties (the key the Minecraft mod templates use) and
// `version = '1.2.3'` or `version '1.2.3'` at the start of a line of
// build.gradle. A line that begins with anything else (`id 'x' version '1'`,
// a dependency) is not the project's own and is not read.
internal static partial class GradleManifest
{
    public static string? PropertiesVersion(string text)
    {
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or '!') continue;

            int separator = line.IndexOfAny(['=', ':']);
            if (separator <= 0) continue;
            if (!string.Equals(line[..separator].Trim(), "mod_version", StringComparison.Ordinal)) continue;
            return VersionText.Plain(line[(separator + 1)..]);
        }
        return null;
    }

    public static string? BuildScriptVersion(string text)
    {
        Match match = Assignment().Match(text.TrimStart('﻿'));
        return match.Success ? VersionText.Plain(match.Groups["value"].Value) : null;
    }

    [GeneratedRegex(@"^[ \t]*version[ \t]*=?[ \t]*(['""])(?<value>[^'""\r\n]*)\1[ \t]*(//.*)?\r?$", RegexOptions.Multiline)]
    private static partial Regex Assignment();
}
