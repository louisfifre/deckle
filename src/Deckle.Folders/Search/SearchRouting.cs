namespace Deckle.Folders;

// What a click on the search button offers.
public enum SearchRoute
{
    // Command Palette is not installed: the panel says so and offers
    // PowerToys' page (12.13, spec Open 9).
    InstallPowerToys,
    // Command Palette is installed: the panel says search is there and gives
    // its shortcut. Microsoft documents no link that only opens the palette
    // (its x-cmdpal:// links run a command or a settings page, after a
    // confirmation), so the app does not open it itself.
    CommandPalette,
}

public static class SearchRouting
{
    // A probe that fails reads as absent: offering PowerToys' page costs the
    // person one look at the Store, while a shortcut that does nothing would
    // leave them with no way to search.
    public static SearchRoute Choose(ICommandPaletteProbe probe)
    {
        SearchRoute route;
        try
        {
            route = probe.IsInstalled() ? SearchRoute.CommandPalette : SearchRoute.InstallPowerToys;
        }
        catch (Exception ex)
        {
            DeckleFoldersSource.Log.CommandPaletteProbeFailed(ex.GetType().Name);
            route = SearchRoute.InstallPowerToys;
        }
        DeckleFoldersSource.Log.SearchRouted(route == SearchRoute.CommandPalette ? "command-palette" : "install-powertoys");
        return route;
    }
}
