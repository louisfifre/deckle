namespace Deckle.Folders;

// Says whether PowerToys Command Palette is installed on this machine. Search
// goes through it (spec, Search), so the search button asks before each
// click: PowerToys installed meanwhile is seen without restarting the app.
// PackagedCommandPaletteProbe is the real one; the self-check and the tests
// give their own answer.
public interface ICommandPaletteProbe
{
    // May block on Windows' package list: callers run it off the UI thread.
    bool IsInstalled();
}
