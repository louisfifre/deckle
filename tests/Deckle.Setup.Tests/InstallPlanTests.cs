using Deckle.Modules;
using Deckle.Transcription;
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

    // Install mode: the plan then reads no settings, so the machine's own do not leak into the test.
    private SetupContext ContextWith(params string[] modules) => new()
    {
        InstallMode = true,
        DataDirectory = _folder,
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

    private const ParakeetEncoderPrecision Fp32 = ParakeetEncoderPrecision.Fp32;

    [Fact]
    public void The_Parakeet_item_weighs_the_catalog_at_the_chosen_precision()
    {
        Assert.Equal(ParakeetModelCatalog.TotalBytes, InstallPlan.ParakeetModelItem(_folder, Fp32).SizeBytes);
        Assert.Equal(
            ParakeetModelCatalog.TotalBytesFor(ParakeetEncoderPrecision.Fp16),
            InstallPlan.ParakeetModelItem(_folder, ParakeetEncoderPrecision.Fp16).SizeBytes);
    }

    [Fact]
    public void The_Parakeet_item_is_not_installed_while_a_pinned_file_is_missing_or_cut_short()
    {
        InstallItem item = InstallPlan.ParakeetModelItem(_folder, Fp32);
        Assert.False(item.IsInstalled());

        // Every file the loader opens, but not at the pinned size: a download cut short.
        var files = new ParakeetModelFiles(_folder);
        foreach (string path in files.RequiredFor(Fp32))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }
        Assert.False(item.IsInstalled());
    }

    [Fact]
    public void The_install_mode_plan_reads_no_settings_and_asks_only_the_chosen_data_root()
    {
        var context = new SetupContext
        {
            InstallMode = true,
            DataDirectory = _folder,
            SelectedModules = new HashSet<string>([ModuleIds.Transcription], StringComparer.Ordinal),
        };

        Assert.True(InstallPlan.IsParakeetModelPending(context));
        Assert.False(Directory.Exists(Path.Combine(_folder, "modules")));
    }
}