using Deckle.Modules;
using Deckle.Transcription.Parakeet;
using Xunit;

namespace Deckle.Setup.Tests;

// What the wizard installs for the Transcription module: the Parakeet speech
// model and the Silero voice-detection model, nothing native and no choice of
// model. Nothing here downloads.
[Trait("Category", "integration")]
public sealed class InstallPlanTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"deckle-install-plan-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    private static SetupContext ContextWith(params string[] modules) => new()
    {
        SelectedModules = new HashSet<string>(modules, StringComparer.Ordinal),
    };

    [Fact]
    public void Transcription_installs_the_Parakeet_model_then_Silero()
    {
        IReadOnlyList<InstallItem> plan = InstallPlan.Build(ContextWith(ModuleIds.Transcription));

        Assert.Equal(
            [InstallPlan.ParakeetModelItemId, InstallPlan.SileroItemId],
            plan.Select(item => item.Id));
    }

    [Fact]
    public void A_plan_without_Transcription_has_no_speech_model()
    {
        IReadOnlyList<InstallItem> plan = InstallPlan.Build(ContextWith(ModuleIds.Anytype));

        Assert.DoesNotContain(plan, item => item.Id == InstallPlan.ParakeetModelItemId);
        Assert.DoesNotContain(plan, item => item.Id == InstallPlan.SileroItemId);
    }

    [Fact]
    public void The_Parakeet_item_weighs_the_catalog()
    {
        InstallItem item = InstallPlan.ParakeetModelItem(() => _folder);

        Assert.Equal(ParakeetModelCatalog.TotalBytes, item.SizeBytes);
    }

    [Fact]
    public void The_Parakeet_item_is_installed_once_every_file_of_the_model_folder_is_there()
    {
        InstallItem item = InstallPlan.ParakeetModelItem(() => _folder);
        Assert.False(item.IsInstalled());

        var files = new ParakeetModelFiles(_folder);
        foreach (string path in files.RequiredFiles.SkipLast(1))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }
        Assert.False(item.IsInstalled());

        string last = files.RequiredFiles[^1];
        Directory.CreateDirectory(Path.GetDirectoryName(last)!);
        File.WriteAllText(last, "x");
        Assert.True(item.IsInstalled());
    }
}
