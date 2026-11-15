using System.Globalization;
using Deckle.Localization;

namespace Deckle.Projects.Views;

// The window's strings read from code, out of this library's own catalog
// (Strings/en-US/Resources.resw). Deckle Projects is written in English
// (spec: the labels on screen are the ones the spec quotes), so counts and
// dates follow en-US, which is also what makes "1,880" read with its comma.
//
// Every page of the window reads its words here, and the catalog is one file
// for the whole library (Loc.GetFrom reads "<library>/Resources" and nothing
// else), so a key is named for the page that owns it: Home_, Category_,
// Project_, Pinned_, Menu_, Pane_.
internal static class ProjectsText
{
    private const string Library = "Deckle.Projects.Views";

    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("en-US");

    public static string Get(string key) => Loc.GetFrom(Library, key);

    public static string Format(string key, params object?[] args) =>
        string.Format(Culture, Get(key), args);
}
