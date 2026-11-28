using Deckle.Localization;

namespace Deckle.Browser.Views;

// The window's words read from code, from the module's own Resources.resw
// (Loc.GetFrom), as Mail's window reads its own.
internal static class BrowserText
{
    private const string Library = "Deckle.Browser.Views";

    public static string Get(string key) => Loc.GetFrom(Library, key);
}
