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
            Exception? rollbackFailure = null;
            if (selectionCaptured && targetSelected)
            {
                try { selection.Restore(previous); }
                catch (Exception ex) { rollbackFailure = ex; }
            }

            try { copier.RollBack(target, source); }
            catch (Exception ex) { rollbackFailure ??= ex; }

            if (rollbackFailure is not null)
                throw new AggregateException(failure, rollbackFailure);

            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }
}
