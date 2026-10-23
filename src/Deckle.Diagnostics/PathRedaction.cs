using System.Collections.Immutable;

namespace Deckle.Diagnostics;

// Hides where on the machine a path sits before it reaches a line. A path under
// the Windows profile names the person (C:\Users\<name>\...), and a journal is
// read on another machine, so a provider passes every absolute path, and every
// exception message that may quote one, through Redact before it writes the event.
// The path keeps what a reader needs, the file under a known root, and loses
// the part that identifies the account.
//
// Why a helper the providers call and not a pass inside the serializer. The
// serializer sees a payload as an untyped value: it cannot tell a path from a
// sentence that happens to start with a drive letter, and a blanket rewrite of
// every string would also rewrite text that must stay verbatim. A provider knows
// which of its parameters is a path, so it is the one place that can name it.
//
// Roots, narrowest first. The data root (%DECKLE_DATA%) sits inside the profile,
// so it is tested before the profile (%USERPROFILE%), and the reader sees
// "%DECKLE_DATA%\models\x.bin" rather than "%USERPROFILE%\AppData\Local\Deckle\...".
// The data root is a folder the host knows (AppPaths.UserDataRoot, possibly moved
// by the person), and Deckle.Diagnostics sits below Deckle.Core, so the host
// registers it with AddRoot once it has resolved it; the profile root is read from
// the OS here. A root the host never registered only costs precision: the path is
// still redacted by the profile root.
//
// A root matches only where it ends: "C:\Users\Lou" does not match inside
// "C:\Users\Louis\x", because the next character must not be a letter or digit.
// The match is case-insensitive, like the Windows file system.
public static class PathRedaction
{
    public const string UserProfileToken = "%USERPROFILE%";
    public const string DataRootToken = "%DECKLE_DATA%";

    private static ImmutableArray<(string Root, string Token)> s_added = ImmutableArray<(string, string)>.Empty;

    // Registers a root the host knows, replaced by `token` wherever it appears.
    // Safe to call from any thread, and more than once with the same root.
    public static void AddRoot(string token, string root)
    {
        if (string.IsNullOrWhiteSpace(token) || !IsUsableRoot(root)) return;
        string trimmed = TrimTrailingSeparators(root);
        ImmutableInterlocked.Update(ref s_added, current =>
            current.Any(r => string.Equals(r.Root, trimmed, StringComparison.OrdinalIgnoreCase))
                ? current
                : current.Add((trimmed, token)));
    }

    // The text with each known root replaced by its token. Null stays null-safe:
    // an absent text comes back empty, because an event payload is never null.
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        string result = text;
        foreach ((string root, string token) in s_added)
            result = Replace(result, root, token);

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (IsUsableRoot(profile))
            result = Replace(result, TrimTrailingSeparators(profile), UserProfileToken);
        return result;
    }

    // Forgets the registered roots, for a test that registers its own.
    internal static void ResetRoots() => s_added = ImmutableArray<(string, string)>.Empty;

    // A drive root such as "C:\" or a one-letter string would swallow far more
    // than a profile folder; refuse it rather than redact half the machine.
    private static bool IsUsableRoot(string? root)
        => !string.IsNullOrWhiteSpace(root) && TrimTrailingSeparators(root).Length >= 4;

    private static string TrimTrailingSeparators(string root)
        => root.TrimEnd('\\', '/');

    private static string Replace(string text, string root, string token)
    {
        // `copied` is how much of the text is already in the result; `search` is
        // where the next look starts. They differ after a match that is skipped
        // because the root is only the start of a longer name.
        int copied = 0;
        int search = 0;
        System.Text.StringBuilder? built = null;
        while (true)
        {
            int at = text.IndexOf(root, search, StringComparison.OrdinalIgnoreCase);
            if (at < 0) break;

            int end = at + root.Length;
            search = end;
            if (end < text.Length && char.IsLetterOrDigit(text[end])) continue;

            built ??= new System.Text.StringBuilder(text.Length);
            built.Append(text, copied, at - copied).Append(token);
            copied = end;
        }

        if (built is null) return text;
        built.Append(text, copied, text.Length - copied);
        return built.ToString();
    }
}
