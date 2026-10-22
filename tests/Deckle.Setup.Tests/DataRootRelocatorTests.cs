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

    // Two undo steps can fail in the same rollback. The caller reads one
    // exception to learn what state the machine is left in, so both must reach it.
    [Fact]
    [Trait("Category", "regression")]
    public void A_failed_restore_and_a_failed_cleanup_are_both_reported()
    {
        var copier = new FakeCopier { RollBackFailure = new IOException("target locked") };
        var selection = new FakeSelection("old") { RestoreFailure = new InvalidOperationException("registry") };
        var launcher = new FakeLauncher { Failure = new InvalidOperationException("launch") };
        var relocator = new DataRootRelocator(copier, selection, launcher);

        var thrown = Assert.Throws<AggregateException>(() => Relocate(relocator));

        Assert.Equal(3, thrown.InnerExceptions.Count);
        Assert.Equal("launch", thrown.InnerExceptions[0].Message);
        Assert.Equal("registry", thrown.InnerExceptions[1].Message);
        Assert.Equal("target locked", thrown.InnerExceptions[2].Message);
    }

    [Fact]
    public void A_failed_cleanup_alone_is_reported_beside_the_failure_that_caused_it()
    {
        var copier = new FakeCopier { RollBackFailure = new IOException("target locked") };
        var selection = new FakeSelection("old");
        var launcher = new FakeLauncher { Failure = new InvalidOperationException("launch") };
        var relocator = new DataRootRelocator(copier, selection, launcher);

        var thrown = Assert.Throws<AggregateException>(() => Relocate(relocator));

        Assert.Equal(2, thrown.InnerExceptions.Count);
        Assert.Equal("launch", thrown.InnerExceptions[0].Message);
        Assert.Equal("target locked", thrown.InnerExceptions[1].Message);
        Assert.Equal("old", selection.Current);
    }

    private static DataRootCopyResult Relocate(DataRootRelocator relocator) =>
        relocator.Relocate("source", "target", 42, progress: null, CancellationToken.None);

    private sealed class FakeCopier : IDataRootCopier
    {
        public DataRootCopyResult Result = new(42, 1, 0);
        public Exception? Failure;
        public int RollBackCalls;
        public Exception? RollBackFailure;

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

        public void RollBack(string target, string source)
        {
            RollBackCalls++;
            if (RollBackFailure is not null) throw RollBackFailure;
        }
    }

    private sealed class FakeSelection(string? current) : IDataRootSelection
    {
        public string? Current = current;
        public Exception? SelectFailure;
        public Exception? RestoreFailure;
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
            if (RestoreFailure is not null) throw RestoreFailure;
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
