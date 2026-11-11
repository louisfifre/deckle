namespace Deckle.Atelier;

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
public readonly record struct RecycleOutcome(RecycleStatus Status, int HResult = 0, string? RecycledPath = null);

// Sends files to the Windows Recycle Bin, one outcome per file, in order.
// The application of the marks depends on this seam, so its plan can be
// exercised without the shell; ShellRecycleBin is the real one.
public interface IRecycleBin
{
    void Recycle(IReadOnlyList<string> paths, Action<int, RecycleOutcome> onEach, CancellationToken cancel);
}
