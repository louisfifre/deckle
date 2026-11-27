using Xunit;

namespace Deckle.Folders.Tests;

// The real Recycle Bin, through the shell, on files and folders the test
// writes in the temporary folder (a drive that has a bin). Each recycled item is removed
// from the bin afterwards, its $R data file and its $I record, so the run
// leaves nothing in the person's bin.
//
// Not covered: a drive without a bin or a file too large for it, where the
// guard must refuse; no such drive can be made from a test.
[Collection(FoldersProviderCollection.Name)]
[Trait("Category", "integration")]
public sealed class ShellRecycleBinTests
{
    [Fact]
    public void AFileGoesToTheRecycleBinAndCanBeFoundThere()
    {
        using var scratch = new ScratchFolder();
        string photo = new ExifSample().WriteJpeg(Path.Combine(scratch.Path, "deckle-folders-recycle-test.jpg"));
        var outcomes = new List<RecycleOutcome>();

        new ShellRecycleBin().Recycle([photo], (_, o) => outcomes.Add(o), CancellationToken.None);

        RecycleOutcome outcome = Assert.Single(outcomes);
        try
        {
            Assert.Equal(RecycleStatus.Recycled, outcome.Status);
            Assert.False(File.Exists(photo));
            Assert.NotNull(outcome.RecycledPath);
            Assert.True(File.Exists(outcome.RecycledPath));
            Assert.Contains("$Recycle.Bin", outcome.RecycledPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            PurgeFromBin(outcome.RecycledPath);
        }
    }

    [Fact]
    public void AFolderGoesToTheRecycleBinWithWhatItHolds()
    {
        using var scratch = new ScratchFolder();
        string folder = scratch.Folder("deckle-folders-recycle-test-folder");
        scratch.File("inside", "deckle-folders-recycle-test-folder", "sub", "note.txt");
        var outcomes = new List<RecycleOutcome>();

        new ShellRecycleBin().Recycle([folder], (_, o) => outcomes.Add(o), CancellationToken.None);

        RecycleOutcome outcome = Assert.Single(outcomes);
        try
        {
            Assert.Equal(RecycleStatus.Recycled, outcome.Status);
            Assert.False(Directory.Exists(folder));
            Assert.NotNull(outcome.RecycledPath);
            Assert.True(File.Exists(Path.Combine(outcome.RecycledPath, "sub", "note.txt")));
        }
        finally
        {
            PurgeFromBin(outcome.RecycledPath);
        }
    }

    [Fact]
    public void EachFileGetsItsOwnOutcomeInOrder()
    {
        using var scratch = new ScratchFolder();
        string first = scratch.File("1", "deckle-folders-recycle-test-1.jpg");
        string missing = Path.Combine(scratch.Path, "gone.jpg");
        string last = scratch.File("3", "deckle-folders-recycle-test-3.jpg");
        var outcomes = new SortedDictionary<int, RecycleOutcome>();

        new ShellRecycleBin().Recycle([first, missing, last], (i, o) => outcomes[i] = o, CancellationToken.None);

        try
        {
            Assert.Equal([RecycleStatus.Recycled, RecycleStatus.Missing, RecycleStatus.Recycled], outcomes.Values.Select(o => o.Status));
        }
        finally
        {
            foreach (RecycleOutcome o in outcomes.Values) PurgeFromBin(o.RecycledPath);
        }
    }

    [Fact]
    public void AStoppedRunLeavesTheRestInPlace()
    {
        using var scratch = new ScratchFolder();
        string photo = scratch.File("1", "deckle-folders-recycle-test.jpg");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var outcomes = new List<RecycleOutcome>();

        new ShellRecycleBin().Recycle([photo], (_, o) => outcomes.Add(o), cancel.Token);

        Assert.Equal(RecycleStatus.NotDone, Assert.Single(outcomes).Status);
        Assert.True(File.Exists(photo));
    }

    // The bin keeps "$R<id>.<ext>" (the data) next to "$I<id>.<ext>" (where it
    // came from and when); a recycled folder's data is a folder. Only the
    // test's own item is touched, by the exact path the shell returned.
    private static void PurgeFromBin(string? recycled)
    {
        if (recycled is null) return;
        string name = Path.GetFileName(recycled);
        if (!name.StartsWith("$R", StringComparison.Ordinal)) return;
        string info = Path.Combine(Path.GetDirectoryName(recycled)!, "$I" + name[2..]);
        try
        {
            if (Directory.Exists(recycled)) Directory.Delete(recycled, recursive: true);
            else File.Delete(recycled);
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(info); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
