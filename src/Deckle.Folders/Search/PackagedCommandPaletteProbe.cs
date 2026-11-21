using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace Deckle.Folders;

// Command Palette ships inside PowerToys as an MSIX package of its own,
// registered for the user when the module is turned on in PowerToys
// (Microsoft Learn: "Command Palette must be enabled and running in the
// background"). Its package family name, Microsoft.CommandPalette_8wekyb3d8bbwe,
// is read on a machine that has it (Get-AppxPackage), not in Microsoft's
// documentation: the family name holds for every version, so it does not move
// with updates, but a later PowerToys could rename the package.
//
// PackageManager.FindPackagesForUser with an empty user id reads the current
// user's registered packages from Windows' own list, the one Get-AppxPackage
// reads: local, no network, no elevation for the current user. A package
// whose files are damaged or still being installed reads as absent, since it
// cannot open.
public sealed class PackagedCommandPaletteProbe : ICommandPaletteProbe
{
    internal const string CommandPaletteFamily = "Microsoft.CommandPalette_8wekyb3d8bbwe";

    private readonly string _family;

    public PackagedCommandPaletteProbe() : this(CommandPaletteFamily) { }

    // Another family, for the tests to point the real query at a package
    // every Windows has, or at one none has.
    internal PackagedCommandPaletteProbe(string family) => _family = family;

    public bool IsInstalled() =>
        new PackageManager().FindPackagesForUser(string.Empty, _family).Any(IsUsable);

    private static bool IsUsable(Package package) => package.Status.VerifyIsOK();
}
