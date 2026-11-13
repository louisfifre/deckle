using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Deckle.Vision;

// ── InterfaceCaptureNames ─────────────────────────────────────────────────────
//
// The names an interface capture gives its folder and its files. They are the
// contract with whoever reads the captures, a review agent most of all: the same
// window or page lands under the same file name from one capture to the next, so
// two folders compare file by file.
//
//   folder   2026-11-07_22-10-03      local time, sortable as text
//   window   log.png, mail.png, hud.png
//   page     settings-recording.png   the window name, then the page's slug
//
// A page slug comes from its page type, the rail's tag: "RecordingPage" becomes
// "recording", "PrecisionScrollPage" "precision-scroll". The type name is used
// rather than the rail label because the label is localized and the file name
// must not change with the language.
public static partial class InterfaceCaptureNames
{
    public const string FileExtension = ".png";
    public const string IndexFileName = "index.json";

    public static string FolderName(DateTimeOffset startedAt)
        => startedAt.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);

    // The page's slug from a rail tag, an assembly-qualified type name such as
    // "Deckle.Transcription.RecordingPage, Deckle.Transcription", or a bare
    // type name.
    public static string PageSlug(string pageTag)
    {
        string typeName = pageTag.Split(',')[0].Trim();
        int dot = typeName.LastIndexOf('.');
        if (dot >= 0) typeName = typeName[(dot + 1)..];
        if (typeName.Length > "Page".Length && typeName.EndsWith("Page", StringComparison.Ordinal))
            typeName = typeName[..^"Page".Length];
        return Slug(typeName);
    }

    // Lowercase ASCII words joined by hyphens. Case changes split words, so
    // "PrecisionScroll" reads "precision-scroll" and "LLMRewrite" "llm-rewrite";
    // anything else that is not a letter or a digit separates words too. An
    // input with no letter or digit gives "window", so a file always has a name.
    public static string Slug(string text)
    {
        string split = WordBoundary().Replace(text, "-");
        var builder = new StringBuilder(split.Length);
        bool pendingHyphen = false;
        foreach (char c in split.Normalize(NormalizationForm.FormD))
        {
            if (char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c < 128 && char.IsLetterOrDigit(c))
            {
                if (pendingHyphen && builder.Length > 0) builder.Append('-');
                builder.Append(char.ToLowerInvariant(c));
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }
        }
        return builder.Length == 0 ? "window" : builder.ToString();
    }

    // The file's base name for a window, or for one page of a paged window.
    public static string BaseName(string window, string? page)
        => page is null ? Slug(window) : $"{Slug(window)}-{page}";

    // The base name, or the first of "name-2", "name-3"… not taken yet. Two
    // captures never overwrite each other within one folder, and the order of
    // capture decides which one keeps the plain name.
    public static string Unique(string baseName, ISet<string> taken)
    {
        string name = baseName;
        for (int n = 2; !taken.Add(name); n++)
            name = $"{baseName}-{n}";
        return name;
    }

    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex WordBoundary();
}
