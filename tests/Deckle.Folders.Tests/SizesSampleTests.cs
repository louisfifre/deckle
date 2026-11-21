using Xunit;

namespace Deckle.Folders.Tests;

// The sizes part of the sample gives the sizes tool something to compare,
// without weighing on the disk the self-check runs on.
[Collection("folders provider singleton")]
[Trait("Category", "integration")]
public sealed class SizesSampleTests
{
    [Fact]
    public void TheSampleWeighsGigabytesSpreadOverFoldersOfDifferentWeights()
    {
        using var scratch = new ScratchFolder();
        string root = SampleFolder.Create(scratch.Path);
        SizesSample.AddTo(root);

        SizeReport report = SizeScan.Start(root).Completion.GetAwaiter().GetResult();

        Assert.True(report.IsComplete);
        Assert.True(report.TotalBytes > 4L * 1024 * 1024 * 1024);
        long?[] sizes = [.. report.Folders.Select(f => f.Bytes)];
        Assert.True(sizes[0] > 10 * sizes[^2]);
        Assert.Contains(report.Heaviest, f => f.FolderRelativeTo(root).Contains(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void ItHoldsAnEmptyFolderAndCanLendAnUnreadableOneThatLeavesNoTrace()
    {
        using var scratch = new ScratchFolder();
        string root = SampleFolder.Create(scratch.Path);
        SizesSample.AddTo(root);

        SizeReport empty = SizeScan.Start(SizesSample.EmptyFolderPath(root)).Completion.GetAwaiter().GetResult();
        string locked;
        SizeReport refused;
        using (SizesSample.AddUnreadableFolder(root, out locked))
            refused = SizeScan.Start(locked).Completion.GetAwaiter().GetResult();

        Assert.True(empty.IsComplete);
        Assert.Empty(empty.Folders);
        Assert.Equal(0, empty.LooseFileCount);
        Assert.Equal(FolderListingFailure.AccessDenied, refused.PlaceFailure);
        Assert.False(Directory.Exists(locked));
    }

    [Fact]
    public void ItsLargeFilesTakeNoRoomOnTheDisk()
    {
        using var scratch = new ScratchFolder();
        string root = SampleFolder.Create(scratch.Path);
        SizesSample.AddTo(root);

        string video = Path.Combine(root, "Vidéos", "Rushes", "C0001.MP4");

        Assert.True(new FileInfo(video).Attributes.HasFlag(FileAttributes.SparseFile));
    }
}
