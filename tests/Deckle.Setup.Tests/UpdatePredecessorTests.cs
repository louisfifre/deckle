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

        public async Task WaitForExitAsync()
        {
            WaitCalls++;
            WaitEntered.TrySetResult();
            await Exit.Task;
        }

        public void Dispose() => Disposed = true;
    }
}
