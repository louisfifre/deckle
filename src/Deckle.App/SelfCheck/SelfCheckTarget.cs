namespace Deckle.App.SelfCheck;

// What a run was asked to look at when it must not walk everything:
// `--self-check <folder> --only <target>`. A target is a window ("mail") or one
// view of a window that has several ("settings/general", "mail/activity",
// "atelier/photos-gallery"): the name its image carries in a full run. It
// exists so a session that changed one view checks that view in seconds
// instead of paying for the whole walk.
//
// How a view is reached depends on its window. A Settings page stands alone and
// is opened directly. A Mail or Atelier view is a state reached from the ones
// before it, so the walk is replayed up to it without a picture, the view is
// photographed, and the walk stops there (SelfCheckWalkGate): the image is the
// one the full run takes, by the same code.
//
// Parsed before any window opens, and pure, so a test hands it text: a target
// that names no window refuses the run with the names it could have been,
// rather than passing on a walk of nothing. Whether the view exists is known
// only once its window has listed its views; the report answers that.
internal sealed record SelfCheckTarget(string Window, string? View)
{
    public const string Flag = "--only";

    // The windows a full run walks, in walk order. The verdict requires each
    // of them of a full run, and the one named of a targeted run.
    public static readonly string[] Windows = ["settings", "log", "transcribe-rewrite", "file-transcription", "mail", "atelier"];

    // The windows whose walk shows more than one view.
    private static readonly string[] WithViews = ["settings", "transcribe-rewrite", "file-transcription", "mail", "atelier"];

    public string Name => View is null ? Window : $"{Window}/{View}";

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
                + $"{string.Join(", ", Windows)}, or <window>/<view> in {string.Join(", ", WithViews)}.";
            return null;
        }
        if (parts.Length == 1) return new SelfCheckTarget(window, null);

        if (Array.IndexOf(WithViews, window) < 0 || parts[1].Length == 0)
        {
            reason = $"'{text}' names no view. A view is <window>/<view> in {string.Join(", ", WithViews)}; "
                + $"{window} alone is walked whole.";
            return null;
        }
        return new SelfCheckTarget(window, parts[1]);
    }
}

// The decision a stateful walk asks before each of its states (Mail's own walk
// decides the same way inside MailWindow). A full walk photographs every
// state. A walk with a target reaches the states before it without a picture,
// photographs the target, and is then done: the caller stops the walk, so
// nothing after the target is opened.
internal sealed class SelfCheckWalkGate(string? view)
{
    // Every state the walk came to, so a target that matched none can be
    // answered with what it could have been.
    public List<string> Offered { get; } = [];

    public bool Reached { get; private set; }

    public bool Done => view is not null && Reached;

    // True when this state gets its picture.
    public bool Photographs(string state)
    {
        Offered.Add(state);
        if (view is null) return true;
        if (state != view) return false;
        Reached = true;
        return true;
    }

    // Called where a state begins: ends a walk whose target has its picture.
    // The steps between two states (leaving a large view, closing a summary)
    // have then run in the state they were written for.
    public void ThrowIfDone()
    {
        if (Done) throw new OperationCanceledException("The self-check's target has its picture.");
    }
}
