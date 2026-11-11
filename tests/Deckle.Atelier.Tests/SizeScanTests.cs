using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace Deckle.Atelier.Tests;

// The Sizes engine on folders made for each test: totals, links, unreadable
// folders, cancelling, running again, the heaviest files and moving down.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class SizeScanTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static string Bytes(int count) => new('x', count);

    private static async Task<SizeReport> ScanAsync(string place, SizeScanOptions? options = null)
        => await SizeScan.Start(place, options).Completion.WaitAsync(Patience);

    [Fact]
    public async Task EachSubFolderCarriesTheSumOfEverythingBeneathIt()
    {
        using var scratch = new ScratchFolder();
        scratch.File(Bytes(10), "loose.txt");
        scratch.File(Bytes(100), "Big", "a.bin");
        scratch.File(Bytes(50), "Big", "Deeper", "b.bin");
        scratch.File(Bytes(5), "Big", "Deeper", "Deepest", "c.bin");
        scratch.File(Bytes(20), "Small", "d.bin");
        scratch.Folder("Empty");

        SizeReport report = await ScanAsync(scratch.Path);

        Assert.Equal(SizeScanState.Completed, report.State);
        Assert.Equal(185, report.TotalBytes);
        Assert.Equal(10, report.LooseFilesBytes);
        Assert.Equal(1, report.LooseFileCount);
        Assert.Equal(["Big", "Small", "Empty"], report.Folders.Select(f => f.Name));
        Assert.Equal([155L, 20L, 0L], report.Folders.Select(f => f.Bytes!.Value));
        Assert.Equal(155.0 / 185, report.Folders[0].Share, precision: 9);
        Assert.Equal(10.0 / 185, report.LooseFilesShare, precision: 9);
        Assert.Equal(0, report.UnreadableFolders);
    }

    [Fact]
    public async Task HiddenAndSystemFilesCountBecauseTheyTakeRoomOnTheDisk()
    {
        using var scratch = new ScratchFolder();
        string hidden = scratch.File(Bytes(30), "Sub", "hidden.bin");
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);
        scratch.File(Bytes(1), "Sub", "shown.bin");

        SizeReport report = await ScanAsync(scratch.Path);

        Assert.Equal(31, report.TotalBytes);
    }

    [Fact]
    public async Task AJunctionIsNotFollowedSoNothingIsCountedTwice()
    {
        using var scratch = new ScratchFolder();
        string target = scratch.File(Bytes(1000), "Real", "data.bin");
        CreateJunction(Path.Combine(scratch.Path, "Link"), Path.GetDirectoryName(target)!);

        SizeScan scan = SizeScan.Start(scratch.Path);
        SizeReport report = await scan.Completion.WaitAsync(Patience);

        Assert.Equal(1000, report.TotalBytes);
        Assert.DoesNotContain(report.Folders, f => f.Name == "Link");
        Assert.Equal(1, scan.Progress.LinksSkipped);
        Assert.Single(report.Heaviest);
    }

    [Fact]
    public async Task AnUnreadableFolderIsCountedAndTheRestIsMeasured()
    {
        using var scratch = new ScratchFolder();
        scratch.File(Bytes(40), "Open", "a.bin");
        string locked = scratch.Folder("Open", "Locked");
        scratch.File(Bytes(7), "Open", "Locked", "hidden-from-us.bin");

        using (DenyListing(locked))
        {
            SizeScan scan = SizeScan.Start(scratch.Path);
            SizeReport report = await scan.Completion.WaitAsync(Patience);

            Assert.Equal(SizeScanState.Completed, report.State);
            Assert.Equal(40, report.TotalBytes);
            Assert.Equal(1, report.UnreadableFolders);
            Assert.Equal(1, report.Folders.Single(f => f.Name == "Open").UnreadableFolders);
            Assert.Equal(1, scan.Progress.UnreadableFolders);
        }
    }

    [Fact]
    public async Task APlaceThatIsGoneFailsAndSaysSo()
    {
        using var scratch = new ScratchFolder();

        SizeReport report = await ScanAsync(Path.Combine(scratch.Path, "gone"));

        Assert.Equal(SizeScanState.Failed, report.State);
        Assert.Equal(FolderListingFailure.NotFound, report.PlaceFailure);
        Assert.Empty(report.Folders);
    }

    [Fact]
    public async Task CancellingStopsPromptlyAndKeepsWhatWasMeasured()
    {
        using var scratch = new ScratchFolder();
        for (int i = 0; i < 60; i++)
            for (int j = 0; j < 40; j++)
                scratch.Folder($"f{i}", $"g{j}");

        SizeScan scan = SizeScan.Start(scratch.Path, new SizeScanOptions { Workers = 1 });
        scan.Cancel();
        var clock = Stopwatch.StartNew();
        SizeReport report = await scan.Completion.WaitAsync(Patience);

        Assert.Equal(SizeScanState.Cancelled, report.State);
        Assert.False(report.IsComplete);
        Assert.True(scan.Progress.FoldersSeen < 60 * 41 + 1, $"folders seen: {scan.Progress.FoldersSeen}");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"stopped after {clock.Elapsed}");
    }

    [Fact]
    public async Task ATokenCancelledBeforehandStopsTheScan()
    {
        using var scratch = new ScratchFolder();
        scratch.File(Bytes(5), "Sub", "a.bin");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        SizeReport report = await SizeScan.Start(scratch.Path, cancel: cancel.Token).Completion.WaitAsync(Patience);

        Assert.Equal(SizeScanState.Cancelled, report.State);
    }

    [Fact]
    public async Task RunningAgainMeasuresTheDiskAsItIsNowAndLeavesTheFirstReportAlone()
    {
        using var scratch = new ScratchFolder();
        scratch.File(Bytes(10), "Sub", "a.bin");
        SizeReport first = await ScanAsync(scratch.Path);

        scratch.File(Bytes(25), "Sub", "b.bin");
        SizeReport second = await ScanAsync(scratch.Path);

        Assert.Equal(10, first.TotalBytes);
        Assert.Equal(35, second.TotalBytes);
        Assert.NotNull(second.FinishedAt);
    }

    [Fact]
    public async Task TheHeaviestFilesComeHeaviestFirstFromAnyDepth()
    {
        using var scratch = new ScratchFolder();
        scratch.File(Bytes(300), "A", "B", "C", "D", "E", "deep.bin");
        scratch.File(Bytes(100), "A", "mid.bin");
        scratch.File(Bytes(200), "top.bin");
        scratch.File(Bytes(50), "A", "B", "small.bin");
        scratch.File(Bytes(1), "tiny.bin");

        SizeReport report = await ScanAsync(scratch.Path, new SizeScanOptions { HeaviestCount = 3 });

        Assert.Equal(["deep.bin", "top.bin", "mid.bin"], report.Heaviest.Select(f => f.Name));
        Assert.Equal([300L, 200L, 100L], report.Heaviest.Select(f => f.Size));
        Assert.Equal(Path.Combine("A", "B", "C", "D", "E"), report.Heaviest[0].FolderRelativeTo(report.Place));
        Assert.Equal("", report.Heaviest[1].FolderRelativeTo(report.Place));
    }

    [Fact]
    public async Task MovingDownIntoAMeasuredFolderNeedsNoNewScan()
    {
        using var scratch = new ScratchFolder();
        scratch.File(Bytes(70), "A", "B", "one.bin");
        scratch.File(Bytes(30), "A", "B", "Sub", "two.bin");
        scratch.File(Bytes(9), "A", "three.bin");
        scratch.File(Bytes(4), "A", "B", "C", "D", "E", "four.bin");

        SizeReport report = await ScanAsync(scratch.Path, new SizeScanOptions { NavigableDepth = 3 });
        SizeReport? b = report.ForFolder(Path.Combine(scratch.Path, "A", "B"));
        SizeReport fresh = await ScanAsync(Path.Combine(scratch.Path, "A", "B"));

        Assert.NotNull(b);
        Assert.Equal(fresh.TotalBytes, b.TotalBytes);
        Assert.Equal(fresh.LooseFilesBytes, b.LooseFilesBytes);
        Assert.Equal(fresh.Folders.Select(f => (f.Name, f.Bytes)), b.Folders.Select(f => (f.Name, f.Bytes)));
        Assert.Equal(fresh.Heaviest.Select(f => f.Path), b.Heaviest.Select(f => f.Path));
        Assert.Same(report, report.ForFolder(scratch.Path));
    }

    [Fact]
    public async Task AFolderDeeperThanTheScanKeptAsksForANewScan()
    {
        using var scratch = new ScratchFolder();
        scratch.File(Bytes(4), "A", "B", "C", "four.bin");

        SizeReport report = await ScanAsync(scratch.Path, new SizeScanOptions { NavigableDepth = 2 });

        Assert.NotNull(report.ForFolder(Path.Combine(scratch.Path, "A", "B")));
        Assert.Null(report.ForFolder(Path.Combine(scratch.Path, "A", "B", "C")));
        Assert.Null(report.ForFolder(Path.GetTempPath()));
    }

    [Fact]
    public async Task OneWorkerAndSeveralFindTheSameSizes()
    {
        using var scratch = new ScratchFolder();
        var random = new Random(7);
        for (int i = 0; i < 200; i++)
            scratch.File(Bytes(random.Next(0, 500)), $"d{i % 7}", $"e{i % 5}", $"f{i}.bin");

        SizeReport one = await ScanAsync(scratch.Path, new SizeScanOptions { Workers = 1 });
        SizeReport many = await ScanAsync(scratch.Path, new SizeScanOptions { Workers = 6 });

        Assert.Equal(one.TotalBytes, many.TotalBytes);
        Assert.Equal(one.Folders.Select(f => (f.Name, f.Bytes)), many.Folders.Select(f => (f.Name, f.Bytes)));
        Assert.Equal(one.Heaviest.Select(f => f.Path), many.Heaviest.Select(f => f.Path));
    }

    [Fact]
    public async Task TheProgressAtTheEndCountsEveryFolderAndFile()
    {
        using var scratch = new ScratchFolder();
        scratch.File(Bytes(3), "A", "x.bin");
        scratch.File(Bytes(4), "A", "B", "y.bin");
        scratch.File(Bytes(5), "C", "z.bin");

        SizeScan scan = SizeScan.Start(scratch.Path);
        await scan.Completion.WaitAsync(Patience);
        SizeProgress progress = scan.Progress;

        Assert.Equal(SizeScanState.Completed, progress.State);
        Assert.Equal(4, progress.FoldersSeen); // the place, A, B, C
        Assert.Equal(3, progress.FilesSeen);
        Assert.Equal(12, progress.BytesSummed);
        Assert.Equal(2, progress.ChildFolders);
        Assert.Equal(2, progress.ChildFoldersMeasured);
    }

    // mklink /J needs no elevation, unlike a directory symbolic link.
    private static void CreateJunction(string link, string target)
    {
        using Process process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        process.WaitForExit();
        Assert.True(Directory.Exists(link), "the junction could not be created");
    }

    // Denies the current user the listing of a folder, and gives it back when
    // disposed so the scratch folder can be removed.
    private static IDisposable DenyListing(string folder)
    {
        var info = new DirectoryInfo(folder);
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory, AccessControlType.Deny);
        DirectorySecurity security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        return new Restore(() =>
        {
            DirectorySecurity back = info.GetAccessControl();
            back.RemoveAccessRule(rule);
            info.SetAccessControl(back);
        });
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
