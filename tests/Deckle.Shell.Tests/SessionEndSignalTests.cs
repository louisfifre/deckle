using Xunit;

namespace Deckle.Shell.Tests;

[Trait("Category", "unit")]
public sealed class SessionEndSignalTests
{
    private const long Logoff   = 0x80000000;
    private const long Critical = 0x40000000;
    private const long CloseApp = 0x00000001;

    [Theory]
    [InlineData(0x0000u)]  // WM_NULL
    [InlineData(0x0010u)]  // WM_CLOSE
    [InlineData(0x0113u)]  // WM_TIMER
    public void Another_message_is_not_a_session_end(uint message)
    {
        Assert.Equal(SessionEndStep.None, SessionEndSignal.Read(message, 1, 0).Step);
    }

    [Fact]
    public void The_question_is_asked_whatever_wParam_says()
    {
        SessionEndSignal signal = SessionEndSignal.Read(SessionEndSignal.WM_QUERYENDSESSION, 0, 0);

        Assert.Equal(SessionEndStep.Asked, signal.Step);
    }

    [Fact]
    public void A_true_verdict_ends_the_session_and_a_false_one_cancels_it()
    {
        Assert.Equal(SessionEndStep.Ending, SessionEndSignal.Read(SessionEndSignal.WM_ENDSESSION, 1, 0).Step);
        Assert.Equal(SessionEndStep.Cancelled, SessionEndSignal.Read(SessionEndSignal.WM_ENDSESSION, 0, 0).Step);
    }

    // The logoff bit is the sign bit of a 32-bit value: it must read as set
    // whether nint is 32 or 64 bits wide, and whichever way the value was cast.
    [Theory]
    [InlineData(Logoff, "logoff", false)]
    [InlineData(Logoff | Critical, "logoff", true)]
    [InlineData(0L, "shutdown", false)]
    [InlineData(Critical, "shutdown", true)]
    [InlineData(CloseApp, "close_app", false)]
    public void The_flags_give_the_reason(long flags, string reason, bool critical)
    {
        SessionEndSignal signal = SessionEndSignal.Read(
            SessionEndSignal.WM_ENDSESSION, 1, unchecked((nint)flags));

        Assert.Equal(reason, signal.Reason);
        Assert.Equal(critical, signal.Critical);
    }

    [Fact]
    public void A_negative_lParam_still_reads_as_logoff()
    {
        SessionEndSignal signal = SessionEndSignal.Read(
            SessionEndSignal.WM_QUERYENDSESSION, 0, unchecked((nint)(int)0x80000000));

        Assert.Equal("logoff", signal.Reason);
    }
}
