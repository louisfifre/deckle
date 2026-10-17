using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "unit")]
public sealed class CrashRelaunchPolicyTests
{
    [Theory]
    [InlineData(unchecked((int)0x80131506))] // CLR execution engine error
    [InlineData(unchecked((int)0xC0000005))] // access violation
    [InlineData(unchecked((int)0xC0000409))] // stack buffer overrun
    [InlineData(unchecked((int)0xE0434352))] // unhandled managed exception
    public void A_failure_status_exit_is_a_crash(int exitCode)
    {
        Assert.True(CrashRelaunchPolicy.IsCrash(exitCode));
    }

    [Theory]
    [InlineData(0)]   // Deckle's own quit and restarts
    [InlineData(1)]   // a kill that picked a small code
    [InlineData(259)]
    [InlineData(0x40010005)] // Ctrl+C status, high bit clear
    public void A_deliberate_or_small_exit_is_not_a_crash(int exitCode)
    {
        Assert.False(CrashRelaunchPolicy.IsCrash(exitCode));
    }

    [Fact]
    public void The_after_crash_value_round_trips()
    {
        string value = CrashRelaunchPolicy.FormatAfterCrash(unchecked((int)0x80131506), 2);

        Assert.True(CrashRelaunchPolicy.TryParseAfterCrash(value, out uint code, out int ordinal));
        Assert.Equal(0x80131506u, code);
        Assert.Equal(2, ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("80131506")]
    [InlineData("zz:1")]
    [InlineData("80131506:-1")]
    public void A_malformed_after_crash_value_is_refused(string? value)
    {
        Assert.False(CrashRelaunchPolicy.TryParseAfterCrash(value, out _, out _));
    }
}

[Trait("Category", "unit")]
public sealed class CrashRelaunchBudgetTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_budget_grants_the_maximum_then_refuses()
    {
        var budget = new CrashRelaunchBudget();

        for (int i = 0; i < CrashRelaunchPolicy.MaxRelaunches; i++)
            Assert.True(budget.TrySpend(Start));

        Assert.False(budget.TrySpend(Start));
        Assert.Equal(CrashRelaunchPolicy.MaxRelaunches, budget.Granted);
    }

    [Fact]
    public void A_relaunch_leaving_the_window_frees_one_slot()
    {
        var budget = new CrashRelaunchBudget();
        for (int i = 0; i < CrashRelaunchPolicy.MaxRelaunches; i++)
            budget.TrySpend(Start);

        Assert.False(budget.TrySpend(Start + CrashRelaunchPolicy.RelaunchWindow - TimeSpan.FromSeconds(1)));
        Assert.True(budget.TrySpend(Start + CrashRelaunchPolicy.RelaunchWindow));
    }
}
