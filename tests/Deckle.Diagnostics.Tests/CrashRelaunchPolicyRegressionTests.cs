using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "regression")]
public sealed class CrashRelaunchPolicyRegressionTests
{
    [Fact]
    public void An_exit_through_Process_Kill_is_not_a_crash()
    {
        // .NET's Process.Kill terminates with -1 (0xFFFFFFFF). It has the high
        // bit like a failure status, but it is a deliberate end.
        Assert.False(CrashRelaunchPolicy.IsCrash(-1));
    }

    [Fact]
    public void The_failure_status_next_to_the_kill_code_is_still_a_crash()
    {
        Assert.True(CrashRelaunchPolicy.IsCrash(unchecked((int)0xFFFFFFFE)));
    }
}
