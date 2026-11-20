namespace Deckle.Projects;

// The line that installs the script menu in a project, the one the page's
// "Copy the install command" link puts on the clipboard. Deckle installs
// nothing itself: the person runs it in a terminal and the page finds the menu
// when the window comes back to the front.
//
// It is the first line of the Install block of the deckle-scripts repository's
// README; the block's second line (install-hooks.ps1) is the maintainer's to
// run. The repository's name is the spec's Open 4 (a possible rename), and
// this constant is the one place that would change.
public static class DeckleScriptsInstall
{
    public const string Command =
        "irm https://raw.githubusercontent.com/louisfifre/deckle-scripts/main/install.ps1 | iex";
}
