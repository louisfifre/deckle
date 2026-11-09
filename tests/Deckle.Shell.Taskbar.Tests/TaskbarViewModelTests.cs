using Deckle.Catalog;
using Microsoft.Win32;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The page's cards against a real registry tree, never the keys Windows reads:
// the tray promoter works on a temporary tree under HKCU\Software\Deckle.Tests\<guid>,
// and the ViewModel loads from the isolated data root. The wording is not under
// test, the .resw key is.
[Trait("Category", "integration")]
[Collection(TaskbarProfileCollection.Name)]
public sealed class TaskbarViewModelTests : IDisposable
{
    private readonly string _testRoot = @"Software\Deckle.Tests\" + Guid.NewGuid().ToString("N");
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "Deckle.Shell.Taskbar.Tests", "vm-" + Guid.NewGuid().ToString("N"));
    private readonly List<TrayIconPromoter> _promoters = [];

    public TaskbarViewModelTests()
    {
        TaskbarSettingsService.Instance.Current.ShowAllTrayIcons = false;
    }

    public void Dispose()
    {
        foreach (TrayIconPromoter promoter in _promoters) promoter.Dispose();
        TaskbarSettingsService.Instance.Current.ShowAllTrayIcons = false;
        Registry.CurrentUser.DeleteSubKeyTree(_testRoot, throwOnMissingSubKey: false);
        try { Directory.Delete(_folder, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    private TaskbarViewModel ViewModel(TrayIconPromoter? tray = null)
    {
        tray ??= Promoter(_testRoot + @"\NotifyIconSettings");
        return new TaskbarViewModel(tray);
    }

    private TrayIconPromoter Promoter(string rootPath)
    {
        var promoter = new TrayIconPromoter(rootPath, nudgeExplorer: false,
            new TraySnapshotStore(Path.Combine(_folder, "tray-snapshot.json")));
        _promoters.Add(promoter);
        return promoter;
    }

    private static SettingDescriptor Card(IReadOnlyList<SettingDescriptor> manifest, string labelKey) =>
        manifest.Single(card => card.LabelKey == labelKey);

    // ── Tray ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TheTrayCardWritesTheSetting()
    {
        TaskbarViewModel viewModel = ViewModel();

        viewModel.ShowAllTrayIcons = true;

        Assert.True(TaskbarSettingsService.Instance.Current.ShowAllTrayIcons);
        Assert.Equal(false, Card(viewModel.TrayManifest, "TaskbarPage_ShowAllTrayIconsCard").Default!());
    }

    [Fact]
    public void ALoadReadsTheSettingWithoutWritingItBack()
    {
        TaskbarSettingsService.Instance.Current.ShowAllTrayIcons = true;
        TaskbarViewModel viewModel = ViewModel();
        Assert.True(viewModel.ShowAllTrayIcons);

        TaskbarSettingsService.Instance.Current.ShowAllTrayIcons = false;
        viewModel.Load();

        Assert.False(viewModel.ShowAllTrayIcons);
    }

    // The page says nothing while the tray works, and which line it says when a
    // pass failed follows the switch: keeping the icons on, or giving them back.
    [Fact]
    public void ATrayThatCouldNotBeKeptCompleteIsToldOnTheCard()
    {
        TrayIconPromoter tray = Promoter(_testRoot + @"\NoSuchKey");
        TaskbarViewModel viewModel = ViewModel(tray);
        Assert.Null(viewModel.TrayMessageKey);

        tray.Apply("setting");
        viewModel.ShowAllTrayIcons = true;

        Assert.Equal("TaskbarPage_TrayFailedOn", viewModel.TrayMessageKey);

        viewModel.ShowAllTrayIcons = false;

        Assert.Equal("TaskbarPage_TrayFailedOff", viewModel.TrayMessageKey);
    }

    // ── Search ───────────────────────────────────────────────────────────────

    // A hit scrolls to the card whose LabelKey it names: a search entry without
    // its card is a dead hit.
    [Fact]
    public void EverySearchEntryNamesACardOfThePage()
    {
        TaskbarViewModel viewModel = ViewModel();
        string[] cards = viewModel.TrayManifest
            .Select(card => card.LabelKey).ToArray();

        Assert.All(SettingsSearch.Entries, entry => Assert.Contains(entry.LabelKey, cards));
        Assert.Equal(cards.Order(), SettingsSearch.Entries.Select(entry => entry.LabelKey).Order());
    }
}