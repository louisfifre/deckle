using Xunit;
using Deckle.Core;

namespace Deckle.Atelier.Tests;

// The gate that holds a scan midway for the self-check: the folders it names
// wait, the rest is measured, and opening it lets the scan end whole.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class SizeScanGateTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task AHeldFolderWaitsWhileTheOthersAreMeasuredThenTheScanEndsWhole(int workers)
    {
        using var scratch = new ScratchFolder();
        scratch.File("abc", "Measured", "a.bin");
        scratch.File("de", "Held", "Deeper", "b.bin");
        string held = Path.Combine(scratch.Path, "Held");
        var gate = new SizeScanGate(folder => FolderPaths.Comparer.Equals(folder, held));

        SizeScan scan = SizeScan.Start(scratch.Path, new SizeScanOptions { Workers = workers, Gate = gate });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!(scan.Progress.ChildFoldersMeasured == 1 && gate.Held == 1) && clock.Elapsed < Patience)
            await Task.Delay(10);
        SizeReport midway = scan.Snapshot();
        gate.Open();
        SizeReport report = await scan.Completion.WaitAsync(Patience);

        Assert.Equal(SizeScanState.Running, midway.State);
        Assert.Equal(3, midway.Folders.Single(f => f.Name == "Measured").Bytes);
        Assert.Null(midway.Folders.Single(f => f.Name == "Held").Bytes);
        Assert.Equal(1, midway.MeasuredFolders);
        Assert.True(report.IsComplete);
        Assert.Equal(5, report.TotalBytes);
        Assert.Equal(2, report.MeasuredFolders);
    }

    [Fact]
    public async Task StoppingWhileAFolderIsHeldEndsTheScanStoppedWithWhatWasMeasured()
    {
        using var scratch = new ScratchFolder();
        scratch.File("abc", "Measured", "a.bin");
        scratch.Folder("Held");
        string held = Path.Combine(scratch.Path, "Held");
        var gate = new SizeScanGate(folder => FolderPaths.Comparer.Equals(folder, held));

        SizeScan scan = SizeScan.Start(scratch.Path, new SizeScanOptions { Gate = gate });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!(scan.Progress.ChildFoldersMeasured == 1 && gate.Held == 1) && clock.Elapsed < Patience)
            await Task.Delay(10);
        scan.Cancel();
        SizeReport report = await scan.Completion.WaitAsync(Patience);

        Assert.Equal(SizeScanState.Cancelled, report.State);
        Assert.Equal(3, report.TotalBytes);
        Assert.Equal(1, report.MeasuredFolders);
        Assert.Equal(2, report.Folders.Count);
    }
}
