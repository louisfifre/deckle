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
    public void TheCompactAppButtonsTakeTheTraysPitchOnAVerticalBarOnly()
    {
        var settings = new TaskbarSettings { CompactTaskButtons = true };

        Assert.Equal(TaskbarCustomization.CompactPitch, From(settings, vertical: true).TaskButtonPitch);
        Assert.Null(From(settings, vertical: false).TaskButtonPitch);
        Assert.False(From(settings, vertical: false).CustomizesExplorer);
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
