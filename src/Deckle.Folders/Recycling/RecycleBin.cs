namespace Deckle.Folders;

public enum RecycleStatus
{
    // The file is in the Windows Recycle Bin and can be restored from it.
    Recycled,
    // The file was already gone when its turn came.
    Missing,
    // Windows could not recycle this file (a drive without a Recycle Bin, a
    // file too large for it): it stays in its folder, never deleted for good.
    Refused,
    // The shell failed on the file; it stays in its folder.
    Failed,
    // The guard did not hold: the file is gone and nothing reached the bin.
    // Reported so the person is told the truth; never expected.
    DeletedForGood,
    // The run was stopped before this file's turn.
    NotDone,
}

// RecycledPath is where the shell put the file inside the bin, when it says.
public readonly record struct RecycleOutcome(RecycleStatus Status, int HResult = 0, string? RecycledPath = null)
{
    // Whether the item is no longer in its folder, whatever took it: a list
    // that showed it lets it go, and one that still has it keeps its row.
    public bool LeftItsFolder => Status is RecycleStatus.Recycled or RecycleStatus.Missing or RecycleStatus.DeletedForGood;
}

// Sends files, or folders with what they hold, to the Windows Recycle Bin,
// one outcome per path, in order.
// The application of the marks and the file tool's row action depend on this
// seam, so what they do can be exercised without the shell; ShellRecycleBin
// is the real one.
public interface IRecycleBin
{
    void Recycle(IReadOnlyList<string> paths, Action<int, RecycleOutcome> onEach, CancellationToken cancel);
}

public static class RecycleBinExtensions
{
    // One item, as the file tool's row action and its Delete key send it: a
    // single path, its single outcome. The shell blocks, so callers run it
    // off the UI thread.
    public static RecycleOutcome RecycleItem(this IRecycleBin bin, string path)
    {
        RecycleOutcome outcome = new(RecycleStatus.NotDone);
        bin.Recycle([path], (_, o) => outcome = o, CancellationToken.None);
        return outcome;
    }
}
