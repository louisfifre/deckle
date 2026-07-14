using Deckle.Setup;
using Xunit;

namespace Deckle.Setup.Tests;

[Trait("Category", "unit")]
public sealed class DataRootRelocatorTests
{
    [Fact]
    public void CopyFailureLeavesTheSelectionAndLauncherUntouched()
    {
        var copier = new FakeCopier { Failure = new IOException("locked settings") };
        var selection = new FakeSelection("old");
        var launcher = new FakeLauncher();

        var relocator = new DataRootRelocator(copier, selection, launcher);

        Assert.Throws<IOException>(() => Relocate(relocator));
        Assert.Equal("old", selection.Current);
        Assert.Equal(0, selection.SelectCalls);
        Assert.Equal(0, launcher.Calls);
        Assert.Equal(1, copier.RollBackCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("old-root")]
    public void LaunchFailureRestoresTheExactPreviousSelection(string? previous)
    {
        var copier = new FakeCopier();
        var selection = new FakeSelection(previous);
        var launcher = new FakeLauncher { Failure = new InvalidOperationException("launch") };
        var relocator = new DataRootRelocator(copier, selection, launcher);

        Assert.Throws<InvalidOperationException>(() => Relocate(relocator));

        Assert.Equal(previous, selection.Current);
        Assert.Equal(1, selection.RestoreCalls);
        Assert.Equal(1, copier.RollBackCalls);
    }

    [Fact]
    public void SelectionFailureAlsoRestoresAndCleansThePreparedTarget()
    {
        var copier = new FakeCopier();
        var selection = new FakeSelection("old") { SelectFailure = new IOException("registry") };
        var launcher = new FakeLauncher();
        var relocator = new DataRootRelocator(copier, selection, launcher);

        Assert.Throws<IOException>(() => Relocate(relocator));

        Assert.Equal("old", selection.Current);
        Assert.Equal(1, selection.RestoreCalls);
        Assert.Equal(1, copier.RollBackCalls);
        Assert.Equal(0, launcher.Calls);
    }

    [Fact]
    public void SuccessfulHandoffCommitsWithoutRollback()
    {
        var copier = new FakeCopier
        {
            Result = new DataRootCopyResult(CopiedBytes: 42, Files: 2, SkippedFiles: 1),
        };
        var selection = new FakeSelection("old");
        var launcher = new FakeLauncher();
        var relocator = new DataRootRelocator(copier, selection, launcher);

        DataRootCopyResult result = Relocate(relocator);

        Assert.Equal(new DataRootCopyResult(42, 2, 1), result);
        Assert.Equal("target", selection.Current);
        Assert.Equal(1, launcher.Calls);
        Assert.Equal(0, selection.RestoreCalls);
        Assert.Equal(0, copier.RollBackCalls);
    }

    private static DataRootCopyResult Relocate(DataRootRelocator relocator) =>
        relocator.Relocate("source", "target", 42, progress: null, CancellationToken.None);

    private sealed class FakeCopier : IDataRootCopier
    {
        public DataRootCopyResult Result = new(42, 1, 0);
        public Exception? Failure;
        public int RollBackCalls;

        public DataRootCopyResult Copy(
            string source,
            string target,
            long totalBytes,
            IProgress<DataRootCopyProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            return Result;
        }

        public void RollBack(string target, string source) => RollBackCalls++;
    }

    private sealed class FakeSelection(string? current) : IDataRootSelection
    {
        public string? Current = current;
        public Exception? SelectFailure;
        public int SelectCalls;
        public int RestoreCalls;

        public string? Capture() => Current;

        public void Select(string target)
        {
            SelectCalls++;
            Current = target;
            if (SelectFailure is not null) throw SelectFailure;
        }

        public void Restore(string? previous)
        {
            RestoreCalls++;
            Current = previous;
        }
    }

    private sealed class FakeLauncher : IDataRootLauncher
    {
        public Exception? Failure;
        public int Calls;

        public void Launch(string target, string source)
        {
            Calls++;
            if (Failure is not null) throw Failure;
        }
    }
}
