using System.ComponentModel;
using Deckle.Catalog;
using Microsoft.Win32;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The page's two cards against a real registry tree and a fake elevation, never
// the keys Windows reads: the Recommended policy works on a temporary tree under
// HKCU\Software\Deckle.Tests\<guid>, the tray promoter on another under it, and
// the ViewModel loads from the isolated data root. The composer calls
// confirmOnEnable with the card's XamlRoot, which the gate does not read, so the
// tests pass null. The wording is not under test, the .resw key is.
[Trait("Category", "integration")]
[Collection(TaskbarProfileCollection.Name)]
public sealed class TaskbarViewModelTests : IDisposable
{
    private readonly string _testRoot = @"Software\Deckle.Tests\" + Guid.NewGuid().ToString("N");
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "Deckle.Shell.Taskbar.Tests", "vm-" + Guid.NewGuid().ToString("N"));
    private readonly PolicyKey _userKey;
    private readonly PolicyKey _machineKey;
    private readonly List<TrayIconPromoter> _promoters = [];
    private readonly Runner _runner;

    public TaskbarViewModelTests()
    {
        _userKey = new PolicyKey(RegistryHive.CurrentUser, _testRoot + @"\User\Explorer");
        _machineKey = new PolicyKey(RegistryHive.CurrentUser, _testRoot + @"\Machine\Explorer");
        _runner = new Runner(_userKey);
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

    private sealed class Runner(PolicyKey userKey) : IElevatedProcessRunner
    {
        public int Calls { get; private set; }
        public bool Declines { get; set; }

        public int Run(string fileName, string arguments)
        {
            Calls++;
            if (Declines) throw new Win32Exception(PolicyElevation.UacDeclined);
            if (arguments.StartsWith("add ", StringComparison.Ordinal))
                userKey.WriteDword(RecommendedSectionPolicy.ValueName, RecommendedSectionPolicy.Hidden);
            else
                userKey.DeleteValue(RecommendedSectionPolicy.ValueName);
            return 0;
        }
    }

    private TaskbarViewModel ViewModel(TrayIconPromoter? tray = null)
    {
        tray ??= Promoter(_testRoot + @"\NotifyIconSettings");
        var policy = new RecommendedSectionPolicy(_userKey, _machineKey, _runner, isPrivileged: false);
        return new TaskbarViewModel(tray, policy);
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

    // ── Recommended section ──────────────────────────────────────────────────

    [Fact]
    public void TheSwitchStartsFromWhatWindowsHolds()
    {
        Assert.False(ViewModel().HideRecommended);

        _userKey.WriteDword(RecommendedSectionPolicy.ValueName, RecommendedSectionPolicy.Hidden);

        Assert.True(ViewModel().HideRecommended);
    }

    // The policy is not a setting: it has no reset, so the page's Reset all
    // never touches it, and no value of it is saved.
    [Fact]
    public void TheCardAsksBeforeItTurnsOnAndHasNoDefault()
    {
        SettingDescriptor card = Card(ViewModel().StartManifest, "TaskbarPage_HideRecommendedCard");

        Assert.NotNull(card.ConfirmOnEnable);
        Assert.Null(card.Default);
    }

    [Fact]
    public async Task AGrantedPromptLetsTheSwitchTurnOn()
    {
        TaskbarViewModel viewModel = ViewModel();
        SettingDescriptor card = Card(viewModel.StartManifest, "TaskbarPage_HideRecommendedCard");

        bool confirmed = await card.ConfirmOnEnable!(null!);

        Assert.True(confirmed);
        Assert.Equal(RecommendedSectionPolicy.Hidden, _userKey.ReadDword(RecommendedSectionPolicy.ValueName));
        Assert.Null(viewModel.RecommendedMessageKey);
    }

    [Fact]
    public async Task ADeclinedPromptKeepsTheSwitchOffAndSaysSo()
    {
        TaskbarViewModel viewModel = ViewModel();
        _runner.Declines = true;

        bool confirmed = await Card(viewModel.StartManifest, "TaskbarPage_HideRecommendedCard").ConfirmOnEnable!(null!);

        Assert.False(confirmed);
        Assert.False(viewModel.HideRecommended);
        Assert.Null(_userKey.ReadDword(RecommendedSectionPolicy.ValueName));
        Assert.Equal("TaskbarPage_RecommendedDeclined", viewModel.RecommendedMessageKey);
    }

    [Fact]
    public async Task TurningTheSwitchOffGivesTheSectionBack()
    {
        _userKey.WriteDword(RecommendedSectionPolicy.ValueName, RecommendedSectionPolicy.Hidden);
        TaskbarViewModel viewModel = ViewModel();

        await viewModel.ShowRecommendedAsync();

        Assert.False(viewModel.HideRecommended);
        Assert.Null(_userKey.ReadDword(RecommendedSectionPolicy.ValueName));
        Assert.Null(viewModel.RecommendedMessageKey);
    }

    // The switch is already off when the prompt opens (the composer writes an
    // off at once); a No brings it back to what Windows still holds.
    [Fact]
    public async Task ADeclinedShowReturnsTheSwitchToWhatWindowsHolds()
    {
        _userKey.WriteDword(RecommendedSectionPolicy.ValueName, RecommendedSectionPolicy.Hidden);
        TaskbarViewModel viewModel = ViewModel();
        viewModel.HideRecommended = false;
        _runner.Declines = true;

        await viewModel.ShowRecommendedAsync();

        Assert.True(viewModel.HideRecommended);
        Assert.Equal("TaskbarPage_RecommendedDeclined", viewModel.RecommendedMessageKey);
    }

    // Someone else's policy: the switch shows on, an off is refused without a
    // prompt, and the card says why before and after the attempt.
    [Fact]
    public async Task AMachinePolicyKeepsTheSwitchOnWithoutAPrompt()
    {
        _machineKey.WriteDword(RecommendedSectionPolicy.ValueName, RecommendedSectionPolicy.Hidden);
        TaskbarViewModel viewModel = ViewModel();
        Assert.True(viewModel.HideRecommended);
        Assert.Equal("TaskbarPage_RecommendedHeldByMachine", viewModel.RecommendedMessageKey);

        viewModel.HideRecommended = false;
        await viewModel.ShowRecommendedAsync();

        Assert.True(viewModel.HideRecommended);
        Assert.Equal("TaskbarPage_RecommendedHeldByMachine", viewModel.RecommendedMessageKey);
        Assert.Equal(0, _runner.Calls);
    }

    [Fact]
    public async Task ARevisitForgetsTheLastRefusal()
    {
        TaskbarViewModel viewModel = ViewModel();
        _runner.Declines = true;
        await Card(viewModel.StartManifest, "TaskbarPage_HideRecommendedCard").ConfirmOnEnable!(null!);

        viewModel.Load();

        Assert.Null(viewModel.RecommendedMessageKey);
    }

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
        string[] cards = viewModel.TrayManifest.Concat(viewModel.StartManifest)
            .Select(card => card.LabelKey).ToArray();

        Assert.All(SettingsSearch.Entries, entry => Assert.Contains(entry.LabelKey, cards));
        Assert.Equal(cards.Order(), SettingsSearch.Entries.Select(entry => entry.LabelKey).Order());
    }
}