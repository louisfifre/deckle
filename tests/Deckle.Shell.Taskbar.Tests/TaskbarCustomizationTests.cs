using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// How the settings page becomes what the native engine is asked to do.
[Trait("Category", "unit")]
public sealed class TaskbarCustomizationTests
{
    private static TaskbarCustomization From(TaskbarSettings settings, bool vertical) =>
        TaskbarCustomization.From(settings, vertical);

    [Fact]
    public void DefaultSettingsCustomizeNothingInEitherProcess()
    {
        TaskbarCustomization customization = From(new TaskbarSettings(), vertical: true);

        Assert.False(customization.CustomizesExplorer);
        Assert.False(customization.CustomizesStartMenu);
    }

    [Fact]
    public void EachPartAloneCustomizesTheProcessItLivesIn()
    {
        Assert.True(From(new TaskbarSettings { Background = TaskbarBackground.Black }, true).CustomizesExplorer);
        Assert.True(From(new TaskbarSettings { CompactClock = true }, true).CustomizesExplorer);
        Assert.True(From(new TaskbarSettings { CompactTray = true }, true).CustomizesExplorer);
        Assert.True(From(new TaskbarSettings { CompactTaskButtons = true }, true).CustomizesExplorer);

        TaskbarCustomization start = From(new TaskbarSettings { StartMenuFullHeight = true }, true);
        Assert.True(start.CustomizesStartMenu);
        Assert.False(start.CustomizesExplorer);
    }

    [Fact]
    public void TheCompactTrayPacksTheIconsAndTheQuickSettingsOnAVerticalBar()
    {
        TaskbarCustomization customization = From(new TaskbarSettings { CompactTray = true }, vertical: true);

        Assert.Equal(24, customization.TrayIconPitch);
        Assert.True(customization.TightenQuickSettings);
    }

    // The tray rules are written for the column layout: on a row they would
    // squash the icons the wrong way, so the setting is kept and nothing is sent.
    [Fact]
    public void TheCompactTrayIsLeftOutOnAHorizontalBar()
    {
        TaskbarCustomization customization = From(new TaskbarSettings { CompactTray = true }, vertical: false);

        Assert.Null(customization.TrayIconPitch);
        Assert.False(customization.TightenQuickSettings);
        Assert.False(customization.CustomizesExplorer);
    }

    [Fact]
    public void TheCompactAppButtonsTakeTheirPitchOnAVerticalBarOnly()
    {
        var settings = new TaskbarSettings { CompactTaskButtons = true };

        Assert.Equal(TaskbarCustomization.DefaultCompactPitch, From(settings, vertical: true).TaskButtonPitch);
        Assert.Null(From(settings, vertical: false).TaskButtonPitch);
        Assert.False(From(settings, vertical: false).CustomizesExplorer);
    }

    // Each card carries its own value: the tray and the app buttons no longer
    // share one pitch.
    [Fact]
    public void EachCompactCardCarriesItsOwnPitch()
    {
        var settings = new TaskbarSettings { CompactTray = true, TrayIconPitch = 28, CompactTaskButtons = true, TaskButtonPitch = 36 };

        TaskbarCustomization customization = From(settings, vertical: true);

        Assert.Equal(28, customization.TrayIconPitch);
        Assert.Equal(36, customization.TaskButtonPitch);
    }

    // A pitch only reaches the engine while its switch is on.
    [Fact]
    public void APitchIsNotSentWhileItsSwitchIsOff()
    {
        var settings = new TaskbarSettings { TrayIconPitch = 28, TaskButtonPitch = 36 };

        Assert.False(From(settings, vertical: true).CustomizesExplorer);
    }

    // A file edited by hand cannot squash an icon to nothing or blow a row up.
    [Theory]
    [InlineData(0, 16, 20)]
    [InlineData(-5, 16, 20)]
    [InlineData(500, 40, 48)]
    public void APitchOutsideItsRangeIsHeldToIt(int written, int tray, int button)
    {
        var settings = new TaskbarSettings { CompactTray = true, TrayIconPitch = written, CompactTaskButtons = true, TaskButtonPitch = written };

        TaskbarCustomization customization = From(settings, vertical: true);

        Assert.Equal(tray, customization.TrayIconPitch);
        Assert.Equal(button, customization.TaskButtonPitch);
    }

    [Fact]
    public void TheStartMenuWidthIsLeftToWindowsByDefault()
    {
        Assert.Null(From(new TaskbarSettings { StartMenuCustomWidth = 900 }, true).StartMenu);
    }

    // Fill carries no number; a custom width carries its own, and either alone
    // reaches the Start menu process, with the height left as it is.
    [Fact]
    public void AStartMenuWidthIsCarriedWithOrWithoutTheFullHeight()
    {
        TaskbarCustomization fill = From(new TaskbarSettings { StartMenuWidth = StartMenuWidth.Fill }, true);
        TaskbarCustomization custom = From(new TaskbarSettings { StartMenuWidth = StartMenuWidth.Custom, StartMenuCustomWidth = 580, StartMenuFullHeight = true }, false);

        Assert.True(fill.CustomizesStartMenu);
        Assert.Equal(StartMenuWidthRequest.Fill, fill.StartMenu!.Width);
        Assert.False(fill.StartMenu.FullHeight);
        Assert.Equal(StartMenuWidthRequest.Of(580), custom.StartMenu!.Width);
        Assert.True(custom.StartMenu.FullHeight);
    }

    // A width written by hand is held to the page's range; the screen's own
    // limit is the engine's, which measures it.
    [Theory]
    [InlineData(0, 320)]
    [InlineData(99999, 1920)]
    public void ACustomWidthOutsideItsRangeIsHeldToIt(int written, int carried)
    {
        var settings = new TaskbarSettings { StartMenuWidth = StartMenuWidth.Custom, StartMenuCustomWidth = written };

        Assert.Equal(StartMenuWidthRequest.Of(carried), From(settings, true).StartMenu!.Width);
    }

    // The clock's lines are the same on both bars; only the tightening of its
    // button is written for the column.
    [Fact]
    public void TheCompactClockTightensItsButtonOnAVerticalBarOnly()
    {
        var settings = new TaskbarSettings { CompactClock = true };

        Assert.True(From(settings, vertical: true).TightenClockButton);
        Assert.False(From(settings, vertical: false).TightenClockButton);
        Assert.NotNull(From(settings, vertical: false).Clock);
    }

    [Fact]
    public void TheBackgroundDoesNotDependOnTheBar()
    {
        var settings = new TaskbarSettings { Background = TaskbarBackground.Transparent };

        Assert.Equal(From(settings, vertical: true), From(settings, vertical: false));
    }
}
