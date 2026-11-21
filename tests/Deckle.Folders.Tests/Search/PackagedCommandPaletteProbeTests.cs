using Xunit;

namespace Deckle.Folders.Tests;

// The real query of Windows' package list, pointed at a family every Windows
// 11 desktop registers for its user (the shell's own) and at one no machine
// has. It shows the query runs unelevated and tells the two apart; whether
// Command Palette itself is installed depends on the machine, so its own
// family name is not asserted here.
[Trait("Category", "integration")]
public sealed class PackagedCommandPaletteProbeTests
{
    [Fact]
    public void APackageEveryWindowsHasReadsAsInstalled()
    {
        var probe = new PackagedCommandPaletteProbe("Microsoft.Windows.ShellExperienceHost_cw5n1h2txyewy");

        Assert.True(probe.IsInstalled());
    }

    [Fact]
    public void APackageNoMachineHasReadsAsAbsent()
    {
        var probe = new PackagedCommandPaletteProbe("Deckle.NoSuchPackage_0000000000000");

        Assert.False(probe.IsInstalled());
    }
}
