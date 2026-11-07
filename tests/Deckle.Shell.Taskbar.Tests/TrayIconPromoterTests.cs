using System.Diagnostics;
using Deckle.TestSupport;
using Microsoft.Win32;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The engine and its watcher against a real registry tree, never the key
// explorer reads: HKCU\Software\Deckle.Tests\<guid>\NotifyIconSettings, handed
// to the constructor as the root and deleted in Dispose. The snapshot is a real
// file in a temporary folder.
//
// In the observability collection: some tests count the provider's events, and
// a milestone carries no payload to tell one test's from another's.
[Trait("Category", "integration")]
[Collection(TaskbarObservabilityCollection.Name)]
public sealed class TrayIconPromoterTests : IDisposable
{
    private static readonly TimeSpan _settle = TimeSpan.FromSeconds(5);

    private readonly string _testRoot = @"Software\Deckle.Tests\" + Guid.NewGuid().ToString("N");
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "Deckle.Shell.Taskbar.Tests", "tray-" + Guid.NewGuid().ToString("N"));
    private readonly List<TrayIconPromoter> _promoters = [];

    public TrayIconPromoterTests()
    {
        Registry.CurrentUser.CreateSubKey(RootPath).Dispose();
    }

    private string RootPath => _testRoot + @"\NotifyIconSettings";

    private string SnapshotPath => Path.Combine(_folder, "tray-snapshot.json");

    public void Dispose()
    {
        foreach (TrayIconPromoter promoter in _promoters) promoter.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_testRoot, throwOnMissingSubKey: false);
        try { Directory.Delete(_folder, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    // A new promoter over the same snapshot file is a new run of Deckle.
    private TrayIconPromoter Promoter(bool nudge = true)
    {
        var promoter = new TrayIconPromoter(RootPath, nudge, new TraySnapshotStore(SnapshotPath));
        _promoters.Add(promoter);
        return promoter;
    }

    private void AddIcon(string subkey, uint? value)
    {
        using RegistryKey icon = Registry.CurrentUser.CreateSubKey(RootPath + @"\" + subkey);
        if (value is uint dword) icon.SetValue(TrayIconPromotion.ValueName, unchecked((int)dword), RegistryValueKind.DWord);
        else icon.DeleteValue(TrayIconPromotion.ValueName, throwOnMissingValue: false);
    }

    private uint? ValueOf(string subkey)
    {
        using RegistryKey? icon = Registry.CurrentUser.OpenSubKey(RootPath + @"\" + subkey);
        return icon?.GetValue(TrayIconPromotion.ValueName) is int value ? unchecked((uint)value) : null;
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < _settle)
        {
            if (condition()) return true;
            Thread.Sleep(25);
        }
        return condition();
    }

    [Fact]
    public void ApplyPromotesEveryIconAndSnapshotsWhatItHeld()
    {
        AddIcon("shown", TrayIconPromotion.Promoted);
        AddIcon("hidden", 0);
        AddIcon("absent", null);

        TrayOutcome outcome = Promoter().Apply("setting");

        Assert.Equal(TrayOutcome.Applied, outcome);
        Assert.All(["shown", "hidden", "absent"], subkey => Assert.Equal(TrayIconPromotion.Promoted, ValueOf(subkey)));
        Assert.Equal(
            new Dictionary<string, uint?> { ["absent"] = null, ["hidden"] = 0, ["shown"] = TrayIconPromotion.Promoted },
            new TraySnapshotStore(SnapshotPath).Entries().ToDictionary(icon => icon.Key, icon => icon.Value));
    }

    [Fact]
    public void ASecondApplyKeepsTheFirstSnapshot()
    {
        AddIcon("hidden", 0);
        TrayIconPromoter promoter = Promoter();
        promoter.Apply("setting");

        promoter.Apply("explorer_restart");

        Assert.Equal(0u, Assert.Single(new TraySnapshotStore(SnapshotPath).Entries()).Value);
    }

    // Deckle restarted with the setting on: the new run finds the snapshot the
    // first one took, whose prior values are Windows's, not Deckle's 1.
    [Fact]
    public void ANewRunKeepsTheSnapshotTheFirstRunTook()
    {
        AddIcon("hidden", 0);
        Promoter().Apply("setting");

        Promoter().Apply("startup");

        Assert.Equal(0u, Assert.Single(new TraySnapshotStore(SnapshotPath).Entries()).Value);
    }

    [Fact]
    public void AnIconAddedWhileWatchingIsPromoted()
    {
        AddIcon("first", 0);
        Promoter().Apply("setting");

        AddIcon("late", null);

        Assert.True(WaitUntil(() => ValueOf("late") == TrayIconPromotion.Promoted), "the new icon was not promoted");
    }

    // Explorer writes 0 back when an icon re-registers, or when the user hides
    // it from Windows Settings: the setting writes 1 again.
    [Fact]
    public void AnIconDemotedWhileWatchingIsPromotedAgain()
    {
        AddIcon("icon", 0);
        Promoter().Apply("setting");

        AddIcon("icon", 0);

        Assert.True(WaitUntil(() => ValueOf("icon") == TrayIconPromotion.Promoted), "the icon stayed demoted");
    }

    [Fact]
    public void RestoreGivesBackTheSnapshotAndDeletesTheValueOfLaterIcons()
    {
        AddIcon("shown", TrayIconPromotion.Promoted);
        AddIcon("hidden", 0);
        AddIcon("absent", null);
        TrayIconPromoter promoter = Promoter();
        promoter.Apply("setting");
        AddIcon("late", 0);
        Assert.True(WaitUntil(() => ValueOf("late") == TrayIconPromotion.Promoted), "the new icon was not promoted");

        TrayOutcome outcome = promoter.Restore();

        Assert.Equal(TrayOutcome.Applied, outcome);
        Assert.Equal(TrayIconPromotion.Promoted, ValueOf("shown"));
        Assert.Equal(0u, ValueOf("hidden"));
        Assert.Null(ValueOf("absent"));
        Assert.Null(ValueOf("late"));
    }

    // The restore comes from the file, so it works for a run that did not take
    // the snapshot: Deckle was restarted, then the setting turned off.
    [Fact]
    public void ARestoreInANewRunGivesBackWhatTheFirstRunSnapshotted()
    {
        AddIcon("hidden", 0);
        Promoter().Apply("setting");
        Assert.Equal(TrayIconPromotion.Promoted, ValueOf("hidden"));

        Promoter().Restore();

        Assert.Equal(0u, ValueOf("hidden"));
    }

    // Once the icons are given back the record is spent: the next on starts
    // from what the registry holds then.
    [Fact]
    public void RestoreForgetsTheSnapshotAndTheNextApplyTakesAFreshOne()
    {
        AddIcon("icon", 0);
        TrayIconPromoter promoter = Promoter();
        promoter.Apply("setting");
        promoter.Restore();
        Assert.False(File.Exists(SnapshotPath));

        AddIcon("icon", TrayIconPromotion.Promoted);
        promoter.Apply("setting");

        Assert.Equal(TrayIconPromotion.Promoted, Assert.Single(new TraySnapshotStore(SnapshotPath).Entries()).Value);
    }

    [Fact]
    public void RestoreStopsTheWatcher()
    {
        AddIcon("icon", 0);
        TrayIconPromoter promoter = Promoter();
        promoter.Apply("setting");
        promoter.Restore();

        AddIcon("late", 0);
        Thread.Sleep(AWatcherTurn());

        Assert.Equal(0u, ValueOf("late"));
    }

    // Quitting Deckle restores nothing and stops watching (brief B8); the
    // snapshot waits in its file for the next off.
    [Fact]
    public void DisposeStopsWatchingAndRestoresNothing()
    {
        AddIcon("icon", 0);
        TrayIconPromoter promoter = Promoter();
        promoter.Apply("setting");

        promoter.Dispose();
        AddIcon("late", 0);
        Thread.Sleep(AWatcherTurn());

        Assert.Equal(TrayIconPromotion.Promoted, ValueOf("icon"));
        Assert.Equal(0u, ValueOf("late"));
        Assert.True(File.Exists(SnapshotPath));
    }

    // An Apply that failed before it could snapshot leaves nothing to tell
    // old icons from new ones: off must not wipe every icon's value.
    [Fact]
    public void RestoreWithNoSnapshotWritesNothing()
    {
        AddIcon("hidden", 0);

        TrayOutcome outcome = Promoter().Restore();

        Assert.Equal(TrayOutcome.Applied, outcome);
        Assert.Equal(0u, ValueOf("hidden"));
    }

    [Fact]
    public void AMissingKeyIsReportedAndRemembered()
    {
        Registry.CurrentUser.DeleteSubKeyTree(RootPath);
        TrayIconPromoter promoter = Promoter();

        Assert.Equal(TrayOutcome.KeyMissing, promoter.Apply("setting"));
        Assert.Equal(TrayOutcome.KeyMissing, promoter.LastOutcome);
    }

    [Fact]
    public void TheLastOutcomeFollowsTheState()
    {
        AddIcon("icon", 0);
        TrayIconPromoter promoter = Promoter();
        int changes = 0;
        promoter.OutcomeChanged += () => Interlocked.Increment(ref changes);
        Assert.Null(promoter.LastOutcome);

        promoter.Apply("setting");
        Assert.Equal(TrayOutcome.Applied, promoter.LastOutcome);
        promoter.Apply("explorer_restart");
        promoter.Restore();

        Assert.Null(promoter.LastOutcome);
        // Applied, then cleared; the second Apply changed nothing.
        Assert.Equal(2, Volatile.Read(ref changes));
    }

    // The first Apply's watcher died on the missing key, and the next Apply
    // kept that dead watcher: it answered Applied while no icon added later
    // was ever promoted.
    [Fact]
    [Trait("Category", "regression")]
    public void AnApplyAfterTheKeyCameBackWatchesAgain()
    {
        Registry.CurrentUser.DeleteSubKeyTree(RootPath);
        TrayIconPromoter promoter = Promoter();
        Assert.Equal(TrayOutcome.KeyMissing, promoter.Apply("setting"));

        AddIcon("first", 0);
        Assert.Equal(TrayOutcome.Applied, promoter.Apply("setting"));
        AddIcon("late", 0);

        Assert.True(WaitUntil(() => ValueOf("late") == TrayIconPromotion.Promoted), "the new icon was not promoted");
    }

    // The key deleted under a running watcher ends its thread; the promoter
    // starts another instead of leaving the outcome Applied over a dead thread.
    [Fact]
    [Trait("Category", "regression")]
    public void AWatcherThatStopsOnItsOwnIsRestarted()
    {
        using var listener = new TestEventListener("Deckle-Taskbar");
        AddIcon("icon", 0);
        Promoter().Apply("setting");

        Registry.CurrentUser.DeleteSubKeyTree(RootPath);

        Assert.True(WaitUntil(() => listener.Events.Any(e =>
            e.EventId == DeckleShellTaskbarSource.EvtTrayWatcherStateChanged
            && Equals(e.Payload?[0], "restarting"))), "the lost watcher was not restarted");
    }

    [Fact]
    public void TheNudgeLeavesNoValueBehind()
    {
        AddIcon("icon", 0);

        Promoter(nudge: true).Apply("setting");

        using RegistryKey icon = Registry.CurrentUser.OpenSubKey(RootPath + @"\icon")!;
        Assert.Equal([TrayIconPromotion.ValueName], icon.GetValueNames());
    }

    // Deckle's own write signals the watcher; the pass that follows must find
    // nothing to write, with or without the nudge. The brief asks for one idle
    // minute; a loop would turn every 200 ms debounce, so a few seconds of
    // silence already rule it out without a minute-long test.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnIdleKeyLogsNoWriteAfterTheWatcherCaughtUp(bool nudge)
    {
        using var listener = new TestEventListener("Deckle-Taskbar");
        AddIcon("icon", 0);
        Promoter(nudge).Apply("setting");
        AddIcon("late", 0);
        Assert.True(WaitUntil(() => ValueOf("late") == TrayIconPromotion.Promoted), "the new icon was not promoted");
        Thread.Sleep(AWatcherTurn());
        int afterCatchUp = WatcherWrites(listener);

        Thread.Sleep(_settle);

        Assert.Equal(1, afterCatchUp);
        Assert.Equal(afterCatchUp, WatcherWrites(listener));
    }

    // Started when the promoter takes hold, Stopped when a restore gives the
    // icons back: the pair a reader of the log pairs on.
    [Fact]
    [Trait("Category", "observability")]
    public void ARunBracketsItsHoldWithAStartAndAStop()
    {
        using var listener = new TestEventListener("Deckle-Taskbar");
        AddIcon("icon", 0);
        TrayIconPromoter promoter = Promoter();

        promoter.Apply("setting");
        promoter.Apply("explorer_restart");
        promoter.Restore();

        int[] milestones = listener.Events
            .Select(e => e.EventId)
            .Where(id => id is DeckleShellTaskbarSource.EvtTrayPromotionStarted or DeckleShellTaskbarSource.EvtTrayPromotionStopped)
            .ToArray();
        Assert.Equal([DeckleShellTaskbarSource.EvtTrayPromotionStarted, DeckleShellTaskbarSource.EvtTrayPromotionStopped], milestones);
    }

    // One Warning for a run of failed passes, closed by one recovery.
    [Fact]
    [Trait("Category", "observability")]
    public void AFailingRunIsOneIncidentClosedByOneRecovery()
    {
        using var listener = new TestEventListener("Deckle-Taskbar");
        Registry.CurrentUser.DeleteSubKeyTree(RootPath);
        TrayIconPromoter promoter = Promoter();

        promoter.Apply("setting");
        promoter.Apply("setting");
        AddIcon("icon", 0);
        promoter.Apply("setting");

        Assert.Single(listener.Events, e => e.EventId == DeckleShellTaskbarSource.EvtTrayPassFailed);
        Assert.Single(listener.Events, e => e.EventId == DeckleShellTaskbarSource.EvtTrayPassRecovered);
    }

    private static int WatcherWrites(TestEventListener listener) =>
        listener.Events.Count(e =>
            e.EventId == DeckleShellTaskbarSource.EvtTrayIconsPromotedDetail
            && Equals(e.Payload?[0], "watcher"));

    // Long enough for a signal to go through the debounce and its pass.
    private static TimeSpan AWatcherTurn() => NotifyIconSettingsWatcher.Debounce * 2 + TimeSpan.FromMilliseconds(300);
}