using Deckle.Catalog;
using Microsoft.Win32;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The page's cards against a real registry tree, never the keys Windows reads:
// the tray promoter works on a temporary tree under HKCU\Software\Deckle.Tests\<guid>,
// and the ViewModel loads from the isolated data root. The customizer is one
// that never starts an engine. The wording is not under test, the .resw key is.
[Trait("Category", "integration")]
[Collection(TaskbarProfileCollection.Name)]
public sealed class TaskbarViewModelTests : IDisposable
{
    private readonly string _testRoot = @"Software\Deckle.Tests\" + Guid.NewGuid().ToString("N");
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "Deckle.Shell.Taskbar.Tests", "vm-" + Guid.NewGuid().ToString("N"));
    private readonly List<TrayIconPromoter> _promoters = [];
    private bool _vertical = true;

    public TaskbarViewModelTests() => Reset();

    public void Dispose()
    {
        foreach (TrayIconPromoter promoter in _promoters) promoter.Dispose();
        Reset();
        Registry.CurrentUser.DeleteSubKeyTree(_testRoot, throwOnMissingSubKey: false);
        try { Directory.Delete(_folder, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    private static void Reset()
    {
        TaskbarSettings settings = TaskbarSettingsService.Instance.Current;
        settings.ShowAllTrayIcons = false;
        settings.Background = TaskbarBackground.Native;
        settings.CompactTray = false;
        settings.CompactTaskButtons = false;
        settings.CompactClock = false;
        settings.StartMenuFullHeight = false;
    }

    // An engine is never created: the settings stay off, and a status is raised by hand.
    private TaskbarCustomizer Customizer(TaskbarSettings? settings = null, Func<TaskbarTarget, ITaskbarEngine>? engines = null) =>
        new(() => settings ?? new TaskbarSettings(), () => _vertical,
            engines ?? (_ => throw new FileNotFoundException("no engine in this test")));

    private TaskbarViewModel ViewModel(TrayIconPromoter? tray = null, TaskbarCustomizer? customizer = null)
    {
        tray ??= Promoter(_testRoot + @"\NotifyIconSettings");
        return new TaskbarViewModel(tray, customizer ?? Customizer(), () => _vertical);
    }

    private TrayIconPromoter Promoter(string rootPath)
    {
        var promoter = new TrayIconPromoter(rootPath, nudgeExplorer: false,
            new TraySnapshotStore(Path.Combine(_folder, "tray-snapshot.json")));
        _promoters.Add(promoter);
        return promoter;
    }

    private static IEnumerable<SettingDescriptor> Cards(TaskbarViewModel viewModel) =>
        viewModel.BarManifest.Concat(viewModel.TrayManifest).Concat(viewModel.StartManifest);

    private static SettingDescriptor Card(TaskbarViewModel viewModel, string labelKey) =>
        Cards(viewModel).Single(card => card.LabelKey == labelKey);

    // ── Writing the settings ─────────────────────────────────────────────────

    [Fact]
    public void EveryCardWritesItsSettingAndDefaultsToOff()
    {
        TaskbarViewModel viewModel = ViewModel();

        viewModel.ShowAllTrayIcons = true;
        viewModel.Background = TaskbarBackground.Transparent;
        viewModel.CompactTray = true;
        viewModel.CompactTaskButtons = true;
        viewModel.CompactClock = true;
        viewModel.StartMenuFullHeight = true;

        TaskbarSettings saved = TaskbarSettingsService.Instance.Current;
        Assert.True(saved.ShowAllTrayIcons);
        Assert.Equal(TaskbarBackground.Transparent, saved.Background);
        Assert.True(saved.CompactTray);
        Assert.True(saved.CompactTaskButtons);
        Assert.True(saved.CompactClock);
        Assert.True(saved.StartMenuFullHeight);

        Assert.Equal(false, Card(viewModel, "TaskbarPage_ShowAllTrayIconsCard").Default!());
        Assert.Equal(TaskbarBackground.Native, Card(viewModel, "TaskbarPage_BackgroundCard").Default!());
        Assert.Equal(false, Card(viewModel, "TaskbarPage_CompactTrayCard").Default!());
        Assert.Equal(false, Card(viewModel, "TaskbarPage_CompactTaskButtonsCard").Default!());
        Assert.Equal(false, Card(viewModel, "TaskbarPage_CompactClockCard").Default!());
        Assert.Equal(false, Card(viewModel, "TaskbarPage_StartMenuFullHeightCard").Default!());
    }

    [Fact]
    public void ALoadReadsTheSettingsWithoutWritingThemBack()
    {
        TaskbarSettings settings = TaskbarSettingsService.Instance.Current;
        settings.ShowAllTrayIcons = true;
        settings.Background = TaskbarBackground.Black;
        settings.CompactClock = true;
        TaskbarViewModel viewModel = ViewModel();
        Assert.True(viewModel.ShowAllTrayIcons);
        Assert.Equal(TaskbarBackground.Black, viewModel.Background);
        Assert.True(viewModel.CompactClock);

        Reset();
        viewModel.Load();

        Assert.False(viewModel.ShowAllTrayIcons);
        Assert.Equal(TaskbarBackground.Native, viewModel.Background);
        Assert.False(viewModel.CompactClock);
    }

    // ── What the page says ───────────────────────────────────────────────────

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

    [Fact]
    public void TheCompactTraySaysItDoesNothingOnAHorizontalBarOnlyWhileItIsOn()
    {
        _vertical = false;
        TaskbarViewModel viewModel = ViewModel();
        Assert.Null(viewModel.CompactTrayMessageKey);

        viewModel.CompactTray = true;
        Assert.Equal("TaskbarPage_CompactTrayHorizontal", viewModel.CompactTrayMessageKey);

        _vertical = true;
        Assert.Null(viewModel.CompactTrayMessageKey);
    }

    [Fact]
    public void TheCompactAppButtonsSayTheyDoNothingOnAHorizontalBarOnlyWhileOn()
    {
        _vertical = false;
        TaskbarViewModel viewModel = ViewModel();
        Assert.Null(viewModel.CompactTaskButtonsMessageKey);

        viewModel.CompactTaskButtons = true;
        Assert.Equal("TaskbarPage_CompactTrayHorizontal", viewModel.CompactTaskButtonsMessageKey);

        _vertical = true;
        Assert.Null(viewModel.CompactTaskButtonsMessageKey);
    }

    // The customizer says a part is in error (here: no engine could be started);
    // the page says so under that card and under no other, and only while it is on.
    [Fact]
    public async Task APartTheEngineCouldNotApplyIsToldOnItsCardAlone()
    {
        var settings = new TaskbarSettings { CompactClock = true };
        TaskbarCustomizer customizer = Customizer(settings);
        await customizer.ReconcileAsync("startup");
        TaskbarViewModel viewModel = ViewModel(customizer: customizer);
        viewModel.CompactClock = true;

        Assert.Equal("TaskbarPage_ProblemError", viewModel.ProblemKey(TaskbarPart.CompactClock));
        Assert.Null(viewModel.ProblemKey(TaskbarPart.StartMenu));

        viewModel.CompactClock = false;

        Assert.Null(viewModel.ProblemKey(TaskbarPart.CompactClock));
    }

    [Fact]
    public void NothingIsSaidWhenAllIsFine()
    {
        TaskbarViewModel viewModel = ViewModel();
        viewModel.Background = TaskbarBackground.Black;
        viewModel.CompactTray = true;
        viewModel.CompactClock = true;
        viewModel.StartMenuFullHeight = true;

        Assert.All(Cards(viewModel).Where(card => card.Advisory is not null), card => Assert.Null(card.Advisory!()));
    }

    // ── Search ───────────────────────────────────────────────────────────────

    // A hit scrolls to the card whose LabelKey it names: a search entry without
    // its card is a dead hit.
    [Fact]
    public void EverySearchEntryNamesACardOfThePage()
    {
        string[] cards = Cards(ViewModel()).Select(card => card.LabelKey).ToArray();

        Assert.All(SettingsSearch.Entries, entry => Assert.Contains(entry.LabelKey, cards));
        Assert.Equal(cards.Order(), SettingsSearch.Entries.Select(entry => entry.LabelKey).Order());
    }
}
