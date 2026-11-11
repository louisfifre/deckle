using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// What a Windows module's file says about itself, read from a real one on this
// machine: the identity the symbol server and the engine both go by.
[Trait("Category", "integration")]
public sealed class ModuleIdentityTests
{
    private static readonly string Kernel32 = Path.Combine(Environment.SystemDirectory, "kernel32.dll");

    [Fact]
    public void AWindowsModuleNamesThePdbItIsPublishedUnder()
    {
        ModuleIdentity identity = ModuleIdentity.Read(Kernel32);

        Assert.Equal("kernel32.pdb", identity.PdbName, ignoreCase: true);
        // 32 hex digits of GUID, then the age in hex.
        Assert.Matches("^[0-9A-F]{33,}$", identity.PdbKey!);
        Assert.NotEqual(0u, identity.TimeDateStamp);
        Assert.True(identity.ImageSize > 0);
    }

    [Fact]
    public void TheBytesAtAnOffsetAreTheFilesOwn()
    {
        ModuleIdentity identity = ModuleIdentity.Read(Kernel32);

        // Offset 0 lies in the headers, outside every section.
        Assert.Null(ModuleIdentity.ReadBytes(Kernel32, 0, 16));
        Assert.Null(ModuleIdentity.ReadBytes(Kernel32, identity.ImageSize + 4096, 16));
    }

    [Fact]
    public void TheCacheKeyChangesWithTheBuild()
    {
        var before = new ModuleIdentity(0x6AA3487A, 10055680, "Taskbar.View.pdb", "A1");
        var after = before with { TimeDateStamp = 0x6AB00000 };

        Assert.Equal("Taskbar.View.dll|6AA3487A997000|f", ExplorerFunctionLocator.CacheKey("Taskbar.View.dll", before, "f"));
        Assert.NotEqual(ExplorerFunctionLocator.CacheKey("Taskbar.View.dll", before, "f"), ExplorerFunctionLocator.CacheKey("Taskbar.View.dll", after, "f"));
    }
}
