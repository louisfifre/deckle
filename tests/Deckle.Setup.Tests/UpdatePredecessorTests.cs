using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Setup.Tests;

public sealed class UpdatePredecessorTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", "regression")]
    public async Task Update_handoff_waits_for_the_exact_predecessor_exit_signal()
    {
        string executable = Path.GetFullPath(Path.Combine("installed", "Deckle.exe"));
        var predecessor = new FakePredecessorProcess(executable);
        var source = new FakePredecessorProcessSource(predecessor);

        Task pending = UpdatePredecessor.WaitForExitAsync(
            int.MaxValue, executable, source, TimeSpan.FromMinutes(1));
        await predecessor.WaitEntered.Task.WaitAsync(Ct);

        Assert.False(pending.IsCompleted);
        predecessor.Exit.TrySetResult();
        await pending;
        Assert.True(predecessor.Disposed);
    }

    [Fact]
    [Trait("Category", "unit")]
    public async Task Reused_pid_with_another_image_is_not_a_predecessor()
    {
        string expected = Path.GetFullPath(Path.Combine("installed", "Deckle.exe"));
        var process = new FakePredecessorProcess(
            Path.GetFullPath(Path.Combine("other", "Deckle.exe")));

        await UpdatePredecessor.WaitForExitAsync(
            int.MaxValue, expected, new FakePredecessorProcessSource(process), TimeSpan.FromMinutes(1));

        Assert.Equal(0, process.WaitCalls);
        Assert.True(process.Disposed);
    }

    // The hand-off wait that ran out means the old app was still alive: a
    // degradation the always-on journal must keep, so a Warning, not detail.
    [Fact]
    [Trait("Category", "observability")]
    public async Task A_predecessor_that_outlives_the_wait_is_reported_as_a_warning()
    {
        string executable = Path.GetFullPath(Path.Combine("installed", "Deckle.exe"));
        var predecessor = new FakePredecessorProcess(executable);
        using var listener = new TestEventListener("Deckle-Setup");

        await UpdatePredecessor.WaitForExitAsync(
            int.MaxValue, executable, new FakePredecessorProcessSource(predecessor),
            TimeSpan.FromMilliseconds(50));

        EventWrittenEventArgs timedOut = Assert.Single(
            listener.Events, e => e.EventId == DeckleSetupSource.EvtPredecessorWaitTimedOut);
        Assert.Equal(EventLevel.Warning, timedOut.Level);
        Assert.Equal(int.MaxValue, timedOut.Payload?[0]);
        Assert.Equal(50L, timedOut.Payload?[1]);
    }

    // WaitForExitAsync can fail for a reason that is not a timeout (the OS
    // refuses the handle, the process object is unusable). The hand-off is
    // best effort and the running-process gate stays authoritative, so the
    // update must go on, with the cause left in the journal.
    [Theory]
    [Trait("Category", "regression")]
    [InlineData(typeof(System.ComponentModel.Win32Exception))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task A_wait_that_fails_does_not_stop_the_update_and_is_reported(Type failure)
    {
        string executable = Path.GetFullPath(Path.Combine("installed", "Deckle.exe"));
        var predecessor = new FakePredecessorProcess(executable)
        {
            WaitFailure = (Exception)Activator.CreateInstance(failure, "access refused")!,
        };
        using var listener = new TestEventListener("Deckle-Setup");

        await UpdatePredecessor.WaitForExitAsync(
            int.MaxValue, executable, new FakePredecessorProcessSource(predecessor),
            TimeSpan.FromMinutes(1));

        EventWrittenEventArgs failed = Assert.Single(
            listener.Events, e => e.EventId == DeckleSetupSource.EvtPredecessorWaitFailed);
        Assert.Equal(EventLevel.Warning, failed.Level);
        Assert.Equal(["pid", "ex_type", "hresult"], failed.PayloadNames);
        Assert.Equal(int.MaxValue, failed.Payload?[0]);
        Assert.Equal(failure.Name, failed.Payload?[1]);
        Assert.True(predecessor.Disposed);
    }

    private sealed class FakePredecessorProcessSource(IUpdatePredecessorProcess process)
        : IUpdatePredecessorProcessSource
    {
        public IUpdatePredecessorProcess? Open(int processId) => process;
    }

    private sealed class FakePredecessorProcess(string executablePath) : IUpdatePredecessorProcess
    {
        public string ExecutablePath { get; } = executablePath;
        public TaskCompletionSource WaitEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Exit { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int WaitCalls { get; private set; }
        public bool Disposed { get; private set; }
        public Exception? WaitFailure { get; init; }

        public async Task WaitForExitAsync()
        {
            WaitCalls++;
            WaitEntered.TrySetResult();
            if (WaitFailure is not null) throw WaitFailure;
            await Exit.Task;
        }

        public void Dispose() => Disposed = true;
    }
}
