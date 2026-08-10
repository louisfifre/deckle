using Deckle.Setup;
using Xunit;

namespace Deckle.Setup.Tests;

public sealed class PayloadDeploymentTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", "regression")]
    public async Task Payload_replacement_preserves_legacy_provider_and_removes_obsolete_app_entries()
    {
        string root = Path.Combine(Path.GetTempPath(), $"deckle-deploy-{Guid.NewGuid():N}");
        string source = Path.Combine(root, "source");
        string target = Path.Combine(root, "target");
        string legacy = Path.Combine(target, "anytype");
        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(legacy);
            await File.WriteAllTextAsync(Path.Combine(source, "Deckle.exe"), "new", Ct);
            await File.WriteAllTextAsync(Path.Combine(target, "Deckle.exe"), "old", Ct);
            await File.WriteAllTextAsync(Path.Combine(target, "obsolete.dll"), "obsolete", Ct);
            await File.WriteAllTextAsync(Path.Combine(legacy, "anytype.exe"), "running", Ct);

            var context = new SetupContext
            {
                SourceDirectory = source,
                InstallDirectory = target,
            };

            DeployPage.CopyPayload(context, legacy);

            Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(target, "Deckle.exe"), Ct));
            Assert.False(File.Exists(Path.Combine(target, "obsolete.dll")));
            Assert.Equal("running", await File.ReadAllTextAsync(Path.Combine(legacy, "anytype.exe"), Ct));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
