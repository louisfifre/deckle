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
public sealed class CrashRelaunchAllowanceTests
{
    // The launcher's chain, as CrashSupervisor drives it: a launcher that has
    // made no relaunch may make one, and the relaunched Deckle's crash comes
    // back to a launcher that has made it. The rule takes no clock, so no
    // time away from the crash can give the allowance back.
    [Fact]
    public void A_launcher_relaunches_once()
    {
        Assert.Equal(1, CrashRelaunchPolicy.MaxRelaunches);
        Assert.True(CrashRelaunchPolicy.MayRelaunch(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void The_crash_of_a_relaunched_Deckle_stays_closed(int relaunchesMade)
    {
        Assert.False(CrashRelaunchPolicy.MayRelaunch(relaunchesMade));
    }

    // Not tested here: a fresh start by the user resets the allowance because
    // it is a new launcher process whose count starts at zero. That is process
    // state in CrashSupervisor, which spawns real processes and has no seam.
}
