using Deckle.Orchestration;
using Deckle.Shell.Foreground;
using Xunit;

namespace Deckle.Orchestration.Tests;

[Trait("Category", "unit")]
public sealed class ForegroundChangeMappingTests
{
    private static ForegroundState Snapshot(
        nint hwnd = 0x1234, uint pid = 4242, string name = "Hollow",
        string device = @"\\.\DISPLAY1", FullscreenStage stage = FullscreenStage.None) =>
        new ForegroundState(hwnd, pid, name, 0x99, default, default, stage) { MonitorDeviceName = device };

    [Fact]
    public void ASnapshotBecomesTheChangeTheRuntimeMatchesOn()
    {
        ForegroundChange change = ForegroundChangeMapping.ToChange(
            Snapshot(), @"C:\Games\Hollow\Hollow.exe");

        Assert.Equal((nint)0x1234, change.WindowHandle);
        Assert.Equal(4242, change.ProcessId);
        Assert.Equal("Hollow", change.ProcessName);
        Assert.Equal(@"C:\Games\Hollow\Hollow.exe", change.ExecutablePath);
        Assert.False(change.IsFullscreen);
        Assert.Equal(@"\\.\DISPLAY1", change.MonitorDeviceName);
    }

    [Theory]
    [InlineData(FullscreenStage.None, false)]
    [InlineData(FullscreenStage.NotificationState, true)]
    [InlineData(FullscreenStage.Geometry, true)]
    public void EveryDetectedFullscreenStageReadsAsFullscreen(FullscreenStage stage, bool expected)
    {
        ForegroundChange change = ForegroundChangeMapping.ToChange(Snapshot(stage: stage), null);

        Assert.Equal(expected, change.IsFullscreen);
    }

    [Fact]
    public void UnknownNamePathAndMonitorReadAsNull()
    {
        ForegroundChange change = ForegroundChangeMapping.ToChange(Snapshot(name: "", device: ""), null);

        Assert.Null(change.ProcessName);
        Assert.Null(change.ExecutablePath);
        Assert.Null(change.MonitorDeviceName);
    }

    [Fact]
    public void AnySnapshotWithAProcessIsNewsBeforeTheFirstChange()
    {
        Assert.True(ForegroundChangeMapping.IsNews(null, Snapshot()));
    }

    [Fact]
    public void ASnapshotWithoutWindowOrProcessIsNeverNews()
    {
        Assert.False(ForegroundChangeMapping.IsNews(null, ForegroundState.None));
        Assert.False(ForegroundChangeMapping.IsNews(null, Snapshot(pid: 0)));
    }

    [Fact]
    public void AnotherWindowComingForwardIsNews()
    {
        ForegroundChange previous = ForegroundChangeMapping.ToChange(Snapshot(), null);

        Assert.True(ForegroundChangeMapping.IsNews(previous, Snapshot(hwnd: 0x5678)));
    }

    [Fact]
    public void TheSameWindowTurningFullscreenInPlaceIsNews()
    {
        ForegroundChange previous = ForegroundChangeMapping.ToChange(Snapshot(), null);

        Assert.True(ForegroundChangeMapping.IsNews(previous, Snapshot(stage: FullscreenStage.Geometry)));
    }

    [Fact]
    public void TheSameWindowMovingToAnotherMonitorIsNews()
    {
        ForegroundChange previous = ForegroundChangeMapping.ToChange(Snapshot(), null);

        Assert.True(ForegroundChangeMapping.IsNews(previous, Snapshot(device: @"\\.\DISPLAY2")));
    }

    [Fact]
    public void AMoveThatKeepsWindowVerdictAndMonitorIsNotNews()
    {
        ForegroundChange previous = ForegroundChangeMapping.ToChange(Snapshot(), null);
        ForegroundState moved = Snapshot() with
        {
            MonitorRect = new Deckle.Core.NativeMethods.RECT { left = 0, top = 0, right = 1920, bottom = 1080 },
        };

        Assert.False(ForegroundChangeMapping.IsNews(previous, moved));
    }
}
