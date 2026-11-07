using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

[Trait("Category", "unit")]
public sealed class TrayIconPromotionTests
{
    private static Dictionary<string, uint?> Key(params (string Subkey, uint? Value)[] icons) =>
        icons.ToDictionary(icon => icon.Subkey, icon => icon.Value);

    [Fact]
    public void EveryIconNotAlreadyPromotedIsPromoted()
    {
        var current = Key(("shown", TrayIconPromotion.Promoted), ("hidden", 0), ("absent", null));

        Assert.Equal(["absent", "hidden"], TrayIconPromotion.SubkeysToPromote(current));
    }

    // What keeps the watcher from looping on Deckle's own write.
    [Fact]
    public void AKeyAlreadyPromotedPlansNoWrite()
    {
        var current = Key(("a", TrayIconPromotion.Promoted), ("b", TrayIconPromotion.Promoted));

        Assert.Empty(TrayIconPromotion.SubkeysToPromote(current));
    }

    [Fact]
    public void RestoreGivesEachIconItsSnapshotValue()
    {
        var snapshot = Key(("shown", TrayIconPromotion.Promoted), ("hidden", 0), ("absent", null));
        var current = Key(
            ("shown", TrayIconPromotion.Promoted), ("hidden", TrayIconPromotion.Promoted), ("absent", TrayIconPromotion.Promoted));

        Assert.Equal(
            [new TrayIconRestoreStep("absent", null), new TrayIconRestoreStep("hidden", 0)],
            TrayIconPromotion.PlanRestore(snapshot, current));
    }

    // Brief question D7, answered by Louis: an icon that appeared after enable
    // goes back to Windows's default, its value deleted.
    [Fact]
    public void RestoreDeletesTheValueOfAnIconTheSnapshotDoesNotKnow()
    {
        var snapshot = Key(("old", 0));
        var current = Key(("old", 0), ("new", TrayIconPromotion.Promoted));

        Assert.Equal([new TrayIconRestoreStep("new", null)], TrayIconPromotion.PlanRestore(snapshot, current));
    }

    [Fact]
    public void RestoreSkipsAnIconWindowsDroppedSinceTheSnapshot()
    {
        var snapshot = Key(("gone", 0), ("kept", 0));
        var current = Key(("kept", TrayIconPromotion.Promoted));

        Assert.Equal([new TrayIconRestoreStep("kept", 0)], TrayIconPromotion.PlanRestore(snapshot, current));
    }

    // Registry key names are case-insensitive.
    [Fact]
    public void RestoreMatchesSubkeysWhateverTheirCase()
    {
        var snapshot = Key(("Icon", 0));
        var current = Key(("ICON", TrayIconPromotion.Promoted));

        Assert.Equal([new TrayIconRestoreStep("ICON", 0)], TrayIconPromotion.PlanRestore(snapshot, current));
    }
}
