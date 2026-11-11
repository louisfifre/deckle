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

    // On 2026-11-11 (12:59) Explorer was restarting and refused one call made
    // while the tray menus were built. The startup stopped there, the UI
    // thread's net kept the process alive, and Deckle ran with no icon and no
    // hotkeys while holding the resident mutex, so no later launch could
    // start. A startup that cannot finish now ends the process with this
    // code, and the launcher must read it as a crash to start Deckle again.
    [Fact]
    public void A_startup_that_could_not_finish_is_relaunched_like_a_crash()
    {
        Assert.True(CrashRelaunchPolicy.IsCrash(CrashRelaunchPolicy.StartupFailedExitCode));
        Assert.NotEqual(-1, CrashRelaunchPolicy.StartupFailedExitCode);
    }

    [Fact]
    public void The_failure_status_next_to_the_kill_code_is_still_a_crash()
    {
        Assert.True(CrashRelaunchPolicy.IsCrash(unchecked((int)0xFFFFFFFE)));
    }
}
