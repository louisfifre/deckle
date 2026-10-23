using System.Runtime.ExceptionServices;

namespace Deckle.Setup;

internal readonly record struct DataRootCopyProgress(long CopiedBytes, long TotalBytes);

internal readonly record struct DataRootCopyResult(long CopiedBytes, int Files, int SkippedFiles);

internal interface IDataRootCopier
{
    DataRootCopyResult Copy(
        string source,
        string target,
        long totalBytes,
        IProgress<DataRootCopyProgress>? progress,
        CancellationToken cancellationToken);

    void RollBack(string target, string source);
}

internal interface IDataRootSelection
{
    string? Capture();
    void Select(string target);
    void Restore(string? previous);
}

internal interface IDataRootLauncher
{
    void Launch(string target, string source);
}

// Owns the commit boundary of a data-root move. A successful process handoff
// commits the move; failures before that point restore the exact previous root
// selection and remove only files created by the copy operation.
internal sealed class DataRootRelocator(
    IDataRootCopier copier,
    IDataRootSelection selection,
    IDataRootLauncher launcher)
{
    public DataRootCopyResult Relocate(
        string source,
        string target,
        long totalBytes,
        IProgress<DataRootCopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        string? previous = null;
        bool selectionCaptured = false;
        bool targetSelected = false;

        try
        {
            DataRootCopyResult result = copier.Copy(
                source, target, totalBytes, progress, cancellationToken);

            previous = selection.Capture();
            selectionCaptured = true;
            targetSelected = true;
            selection.Select(target);

            // Nothing that can trigger rollback belongs after this call. Once
            // the child process is created it owns the prepared target.
            launcher.Launch(target, source);
            return result;
        }
        catch (Exception failure)
        {
            // Each undo step that fails is kept: the caller reads this one
            // exception to learn what state the machine is left in (a root
            // still selected, files still on disk), so dropping the second
            // failure would hide half of it.
            var failures = new List<Exception> { failure };
            if (selectionCaptured && targetSelected)
            {
                try { selection.Restore(previous); }
                catch (Exception ex) { failures.Add(ex); }
            }

            try { copier.RollBack(target, source); }
            catch (Exception ex) { failures.Add(ex); }

            if (failures.Count > 1)
                throw new AggregateException(failures);

            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }
}
