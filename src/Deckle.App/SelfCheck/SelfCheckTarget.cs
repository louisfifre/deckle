namespace Deckle.App.SelfCheck;

// What a run was asked to look at when it must not walk everything:
// `--self-check <folder> --only <target>`. A target is a window ("mail") or one
// page of the Settings window ("settings/general", the slug its image carries).
// It exists so a session that changed one page checks that page in seconds
// instead of paying for the whole walk.
//
// Only Settings takes a page. The Mail and Atelier walks are sequences where a
// state is reached from the one before it, so they are walked whole or not at
// all.
//
// Parsed before any window opens, and pure, so a test hands it text: a target
// that names no window refuses the run with the names it could have been,
// rather than passing on a walk of nothing.
internal sealed record SelfCheckTarget(string Window, string? Page)
{
    public const string Flag = "--only";

    // The windows a full run walks, in walk order. The verdict requires each
    // of them of a full run, and the one named of a targeted run.
    public static readonly string[] Windows = ["settings", "log", "playground", "mail", "atelier"];

    private const string PagedWindow = "settings";

    public string Name => Page is null ? Window : $"{Window}/{Page}";

    // The value after --only, or null when the flag is absent. A flag with
    // nothing after it gives "", which Parse refuses.
    public static string? ReadArgument(string[] args)
    {
        int flag = Array.IndexOf(args, Flag);
        if (flag < 0) return null;
        return flag + 1 < args.Length ? args[flag + 1] : "";
    }

    // Null with a reason when the text names nothing a run can walk.
    public static SelfCheckTarget? Parse(string text, out string reason)
    {
        reason = "";
        string[] parts = text.Trim().ToLowerInvariant().Split('/');
        string window = parts[0];
        if (parts.Length > 2 || Array.IndexOf(Windows, window) < 0)
        {
            reason = $"'{text}' names no window of the self-check. A target is one of "
                + $"{string.Join(", ", Windows)}, or {PagedWindow}/<page>.";
            return null;
        }
        if (parts.Length == 1) return new SelfCheckTarget(window, null);

        if (window != PagedWindow || parts[1].Length == 0)
        {
            reason = $"'{text}' names a page, and only {PagedWindow} takes one ({PagedWindow}/<page>): "
                + $"{window} is walked whole.";
            return null;
        }
        return new SelfCheckTarget(window, parts[1]);
    }
}
