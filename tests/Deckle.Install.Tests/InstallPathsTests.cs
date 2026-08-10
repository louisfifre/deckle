using Xunit;

namespace Deckle.Install.Tests;

public sealed class InstallPathsTests
{
    [Fact]
    [Trait("Category", "regression")]
    public void Provider_ownership_includes_external_and_legacy_executable_roots()
    {
        Assert.Contains(InstallPaths.DefaultProvidersDir, InstallPaths.ProviderDirectories);
        Assert.Contains(InstallPaths.LegacyAnytypeProviderDir, InstallPaths.ProviderDirectories);
        Assert.Equal(
            Path.Combine(InstallPaths.DefaultInstallDir, "anytype"),
            InstallPaths.LegacyAnytypeProviderDir);
    }
}
