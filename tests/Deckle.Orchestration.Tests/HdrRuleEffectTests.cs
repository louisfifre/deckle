using Xunit;

namespace Deckle.Orchestration.Tests;

[Trait("Category", "unit")]
public sealed class HdrRuleEffectTests
{
    [Fact]
    public void AnAppWhoseMonitorsAreAllLeftAloneAsksForNoHdrChange()
    {
        var entry = new AppEntry { ProcessName = "Hollow" };
        Assert.False(new HdrRuleEffect().AppliesTo(entry));

        entry.Rules.HdrByMonitor[@"\\.\DISPLAY1"] = HdrRule.Leave;
        Assert.False(new HdrRuleEffect().AppliesTo(entry));
    }

    [Theory]
    [InlineData(HdrRule.On)]
    [InlineData(HdrRule.Off)]
    public void AnAppWithAnHdrRuleOnAMonitorAsksForAnHdrChange(HdrRule rule)
    {
        var entry = new AppEntry { ProcessName = "Hollow" };
        entry.Rules.HdrByMonitor[@"\\.\DISPLAY1"] = HdrRule.Leave;
        entry.Rules.HdrByMonitor[@"\\.\DISPLAY2"] = rule;

        Assert.True(new HdrRuleEffect().AppliesTo(entry));
    }

    [Fact]
    public void AnHdrRuleForAMonitorThatIsNotPluggedInChangesNothingAndFailsNothing()
    {
        var entry = new AppEntry { ProcessName = "Hollow" };
        entry.Rules.HdrByMonitor[@"\\.\DISPLAY-NOT-PLUGGED-IN"] = HdrRule.On;

        Action? restore = new HdrRuleEffect().Apply(entry);

        Assert.Null(restore);
    }
}
