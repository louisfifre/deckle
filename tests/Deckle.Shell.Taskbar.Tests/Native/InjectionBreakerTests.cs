using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// When the host stops sending the engine into a target that keeps dying. The
// clock is the test's own, so "just after the injection" is a figure, not a wait.
[Trait("Category", "unit")]
public sealed class InjectionBreakerTests
{
    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    private static readonly TimeSpan _quick = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan _slow = InjectionBreaker.QuickLoss + TimeSpan.FromSeconds(1);

    private readonly ManualTime _time = new();

    // One injection, then the engine lost after `after`.
    private bool InjectThenLose(InjectionBreaker breaker, TimeSpan after)
    {
        breaker.Injected();
        _time.Advance(after);
        return breaker.Lost();
    }

    [Fact]
    public void ATargetThatDiesJustAfterEachInjectionOpensTheBreaker()
    {
        var breaker = new InjectionBreaker(_time);

        for (int loss = 1; loss < InjectionBreaker.MaxQuickLosses; loss++)
        {
            Assert.False(InjectThenLose(breaker, _quick));
            Assert.False(breaker.IsOpen);
        }

        Assert.True(InjectThenLose(breaker, _quick));
        Assert.True(breaker.IsOpen);
    }

    // 2026-11-11, 12:59: explorer crashed three seconds after an injection,
    // and the next injection held. A crash that does not repeat changes nothing.
    [Fact]
    public void ASingleQuickLossLeavesTheBreakerClosed()
    {
        var breaker = new InjectionBreaker(_time);

        Assert.False(InjectThenLose(breaker, _quick));
        Assert.False(InjectThenLose(breaker, TimeSpan.FromMinutes(5)));

        Assert.False(breaker.IsOpen);
    }

    // An Explorer restart minutes after the injection is not the engine's doing.
    [Fact]
    public void ATargetThatLivedStartsTheCountAgain()
    {
        var breaker = new InjectionBreaker(_time);
        for (int loss = 1; loss < InjectionBreaker.MaxQuickLosses; loss++) InjectThenLose(breaker, _quick);

        Assert.False(InjectThenLose(breaker, _slow));
        for (int loss = 1; loss < InjectionBreaker.MaxQuickLosses; loss++)
            Assert.False(InjectThenLose(breaker, _quick));

        Assert.False(breaker.IsOpen);
    }

    // Both pipes of a target can close for one death: one injection, one loss.
    [Fact]
    public void ALossIsCountedOncePerInjection()
    {
        var breaker = new InjectionBreaker(_time);

        breaker.Injected();
        for (int i = 0; i < InjectionBreaker.MaxQuickLosses + 1; i++) Assert.False(breaker.Lost());

        Assert.False(breaker.IsOpen);
    }

    [Fact]
    public void AnOpenBreakerSaysSoOnce()
    {
        var breaker = new InjectionBreaker(_time);
        for (int loss = 1; loss < InjectionBreaker.MaxQuickLosses; loss++) InjectThenLose(breaker, _quick);
        Assert.True(InjectThenLose(breaker, _quick));

        Assert.False(InjectThenLose(breaker, _quick));
        Assert.True(breaker.IsOpen);
    }
}
