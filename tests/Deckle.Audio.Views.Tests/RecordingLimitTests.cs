using Deckle.Audio.Views;
using Xunit;

namespace Deckle.Audio.Views.Tests;

// The maximum recording length as the Recording page shows it: a switch and a
// length in minutes over a POCO that keeps seconds. A freshly built
// RecordingViewModel is still syncing, so its setters change the values without
// pushing to the settings service: nothing here touches the disk.
[Trait("Category", "unit")]
public class RecordingLimitTests
{
    [Fact]
    public void StoredZeroIsOffAndShowsTheDefaultLength()
    {
        var vm = new RecordingViewModel { MaxRecordingDurationSeconds = 0 };

        Assert.False(vm.LimitRecordingDuration);
        Assert.Equal(RecordingLimit.DefaultMinutes, vm.MaxRecordingDurationMinutes);
    }

    [Fact]
    public void SwitchingOnWritesTheDefaultLengthInSeconds()
    {
        var vm = new RecordingViewModel();

        vm.LimitRecordingDuration = true;

        Assert.Equal(RecordingLimit.DefaultMinutes * 60, vm.MaxRecordingDurationSeconds);
    }

    [Fact]
    public void OffThenOnRestoresTheLastLength()
    {
        var vm = new RecordingViewModel();
        vm.LimitRecordingDuration = true;
        vm.MaxRecordingDurationMinutes = 45;

        vm.LimitRecordingDuration = false;
        Assert.Equal(0, vm.MaxRecordingDurationSeconds);

        vm.LimitRecordingDuration = true;
        Assert.Equal(45 * 60, vm.MaxRecordingDurationSeconds);
    }

    [Fact]
    public void LengthEditedWhileOffIsKeptForTheNextSwitchOn()
    {
        var vm = new RecordingViewModel();

        vm.MaxRecordingDurationMinutes = 15;
        Assert.Equal(0, vm.MaxRecordingDurationSeconds);

        vm.LimitRecordingDuration = true;
        Assert.Equal(15 * 60, vm.MaxRecordingDurationSeconds);
    }

    [Theory]
    [InlineData(10800, 120)] // 3 h, above the range
    [InlineData(20, 1)]      // 20 s, below one minute
    [InlineData(5400, 90)]   // inside the range, untouched
    public void StoredValueOutsideTheRangeShowsAtTheNearestEnd(int storedSeconds, int shownMinutes)
    {
        var vm = new RecordingViewModel { MaxRecordingDurationSeconds = storedSeconds };

        Assert.True(vm.LimitRecordingDuration);
        Assert.Equal(shownMinutes, vm.MaxRecordingDurationMinutes);
        // The file is left as it was until the person moves the control.
        Assert.Equal(storedSeconds, vm.MaxRecordingDurationSeconds);
    }

    [Fact]
    public void TypedLengthIsClampedToTheRange()
    {
        var vm = new RecordingViewModel();
        vm.LimitRecordingDuration = true;

        vm.MaxRecordingDurationMinutes = 500;
        Assert.Equal(RecordingLimit.MaxMinutes * 60, vm.MaxRecordingDurationSeconds);

        vm.MaxRecordingDurationMinutes = 0;
        Assert.Equal(RecordingLimit.MinMinutes * 60, vm.MaxRecordingDurationSeconds);
    }

    [Fact]
    public void ClearedFieldIsIgnored()
    {
        var vm = new RecordingViewModel();
        vm.LimitRecordingDuration = true;

        vm.MaxRecordingDurationMinutes = double.NaN;

        Assert.Equal(RecordingLimit.DefaultMinutes * 60, vm.MaxRecordingDurationSeconds);
    }
}
