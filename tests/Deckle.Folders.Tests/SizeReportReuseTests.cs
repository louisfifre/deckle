using Xunit;

namespace Deckle.Folders.Tests;

// Which report the sizes tool shows without reading the disk, on scans of
// folders made for each test.
[Collection(FoldersProviderCollection.Name)]
[Trait("Category", "integration")]
public sealed class SizeReportReuseTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static async Task<SizeReport> ScanAsync(string place, SizeScanOptions? options = null)
        => await SizeScan.Start(place, options).Completion.WaitAsync(Patience);

    [Fact]
    public async Task ComingBackShowsTheReportLeftAndMovingDownReRootsTheLastCompleteOne()
    {
        using var scratch = new ScratchFolder();
        scratch.File("abc", "A", "a.bin");
        SizeReport report = await ScanAsync(scratch.Path);

        Assert.Same(report, SizeReportReuse.For(scratch.Path, refresh: false, report, report));
        SizeReport? down = SizeReportReuse.For(Path.Combine(scratch.Path, "A"), refresh: false, report, report);
        Assert.NotNull(down);
        Assert.Equal(3, down.TotalBytes);
    }

    [Fact]
    public async Task ComingBackToAStoppedAnalysisShowsItStoppedAndAFailedOneIsMeasuredAgain()
    {
        using var scratch = new ScratchFolder();
        scratch.File("abc", "A", "a.bin");
        SizeScan stoppedScan = SizeScan.Start(scratch.Path, new SizeScanOptions { Gate = new SizeScanGate(_ => true) });
        stoppedScan.Cancel();
        SizeReport stopped = await stoppedScan.Completion.WaitAsync(Patience);
        SizeReport failed = await ScanAsync(Path.Combine(scratch.Path, "gone"));

        Assert.Same(stopped, SizeReportReuse.For(scratch.Path, refresh: false, stopped, null));
        Assert.Null(SizeReportReuse.For(failed.Place, refresh: false, failed, null));
    }
}

// Refresh on a stopped analysis served the last complete report of the same
// place instead of measuring (SizesBugNotes.md).
[Collection(FoldersProviderCollection.Name)]
[Trait("Category", "regression")]
public sealed class SizeRefreshRegressionTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task RefreshOnAStoppedAnalysisMeasuresAgain()
    {
        using var scratch = new ScratchFolder();
        scratch.File("abc", "A", "a.bin");
        SizeReport complete = await SizeScan.Start(scratch.Path).Completion.WaitAsync(Patience);
        SizeScan again = SizeScan.Start(scratch.Path, new SizeScanOptions { Gate = new SizeScanGate(_ => true) });
        again.Cancel();
        SizeReport stopped = await again.Completion.WaitAsync(Patience);

        Assert.Equal(SizeScanState.Cancelled, stopped.State);
        Assert.Null(SizeReportReuse.For(scratch.Path, refresh: true, stopped, complete));
        Assert.Null(SizeReportReuse.For(scratch.Path, refresh: true, complete, complete));
    }
}
