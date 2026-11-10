using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using static Deckle.Atelier.ShellFileOperationInterop;

namespace Deckle.Atelier;

// Sends files to the Windows Recycle Bin through the shell's IFileOperation,
// so a photo lands where File Explorer would put it and "Restaurer" works.
//
// The shell's file operations want a single-threaded apartment, so a call runs
// on a thread of its own, started for it and joined before it returns.
//
// One operation per file. An error returned from PreDeleteItem cancels the
// whole operation and every one pending after it (Microsoft Learn,
// IFileOperationProgressSink::PreDeleteItem), so a batch would let one file
// that cannot be recycled stop the rest. Per file, it stops only itself.
//
// The guard against a deletion for good, in three layers:
// 1. FOFX_RECYCLEONDELETE asks for the bin.
// 2. PreDeleteItem refuses an item the shell announces without
//    TSF_DELETE_RECYCLE_IF_POSSIBLE, that is one it would destroy.
// 3. FOF_WANTNUKEWARNING: should the shell destroy one anyway, Windows' own
//    "permanently delete?" question appears instead of a silent deletion
//    (it "partially overrides FOF_NOCONFIRMATION", Microsoft Learn).
// PostDeleteItem then tells the outcome: an item created in the bin, or none,
// which means the file was destroyed and is reported as such.
//
// Not verified on this machine: a drive without a Recycle Bin, or a file too
// large for it, to see layer 2 refuse in practice. The tests cover the common
// path, a file on a drive that has a bin.
public sealed partial class ShellRecycleBin : IRecycleBin
{
    private const uint Flags = FOFX_RECYCLEONDELETE | FOF_ALLOWUNDO | FOF_NOCONFIRMATION
                             | FOF_WANTNUKEWARNING | FOF_NOERRORUI | FOF_SILENT;

    public void Recycle(IReadOnlyList<string> paths, Action<int, RecycleOutcome> onEach, CancellationToken cancel)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    onEach(i, cancel.IsCancellationRequested ? new RecycleOutcome(RecycleStatus.NotDone) : RecycleOne(paths[i]));
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "Atelier recycle bin",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("The recycle thread failed.", failure);
    }

    private static RecycleOutcome RecycleOne(string path)
    {
        if (!File.Exists(path)) return new RecycleOutcome(RecycleStatus.Missing);

        int hr = SHCreateItemFromParsingName(path, 0, in IID_IShellItem, out nint item);
        if (hr < 0) return Refused("shell-item", hr, RecycleStatus.Failed);

        nint opPtr = 0;
        try
        {
            hr = CoCreateInstance(in CLSID_FileOperation, 0, CLSCTX_INPROC_SERVER, in IID_IFileOperation, out opPtr);
            if (hr < 0) return Refused("file-operation", hr, RecycleStatus.Failed);

            var wrappers = new StrategyBasedComWrappers();
            var operation = (IFileOperation)wrappers.GetOrCreateObjectForComInstance(opPtr, CreateObjectFlags.UniqueInstance);
            var sink = new DeleteSink(wrappers);

            operation.SetOperationFlags(Flags);
            operation.Advise(sink, out uint cookie);
            operation.DeleteItem(item, 0);
            int performed = operation.PerformOperations();
            operation.Unadvise(cookie);

            bool stillThere = File.Exists(path);
            if (sink.Recycled && !stillThere)
                return new RecycleOutcome(RecycleStatus.Recycled, 0, sink.RecycledPath);
            if (stillThere)
            {
                return sink.Refused
                    ? Refused("not-recyclable", HR_CANCELLED, RecycleStatus.Refused)
                    : Refused("shell-error", performed < 0 ? performed : sink.DeleteResult, RecycleStatus.Failed);
            }
            DeckleAtelierSource.Log.PhotoDeletedForGood(Path.GetExtension(path).ToLowerInvariant());
            return new RecycleOutcome(RecycleStatus.DeletedForGood, sink.DeleteResult);
        }
        catch (COMException ex)
        {
            return Refused("shell-error", ex.HResult, RecycleStatus.Failed);
        }
        finally
        {
            if (opPtr != 0) Marshal.Release(opPtr);
            Marshal.Release(item);
        }
    }

    private static RecycleOutcome Refused(string reason, int hr, RecycleStatus status)
    {
        DeckleAtelierSource.Log.PhotoRecycleRefused(reason, hr);
        return new RecycleOutcome(status, hr);
    }

    [GeneratedComClass]
    private sealed partial class DeleteSink(StrategyBasedComWrappers wrappers) : IFileOperationProgressSink
    {
        public bool Refused { get; private set; }
        public bool Recycled { get; private set; }
        public int DeleteResult { get; private set; }
        public string? RecycledPath { get; private set; }

        public int PreDeleteItem(uint dwFlags, nint psiItem)
        {
            if ((dwFlags & TSF_DELETE_RECYCLE_IF_POSSIBLE) != 0) return S_OK;
            Refused = true;
            return HR_CANCELLED;
        }

        // psiNewlyCreated is the item now in the Recycle Bin; NULL when the
        // item was destroyed (Microsoft Learn, PostDeleteItem).
        public int PostDeleteItem(uint dwFlags, nint psiItem, int hrDelete, nint psiNewlyCreated)
        {
            DeleteResult = hrDelete;
            if (hrDelete < 0 || psiNewlyCreated == 0) return S_OK;
            Recycled = true;
            RecycledPath = PathOf(psiNewlyCreated);
            return S_OK;
        }

        private string? PathOf(nint shellItem)
        {
            try
            {
                var item = (IShellItem)wrappers.GetOrCreateObjectForComInstance(shellItem, CreateObjectFlags.UniqueInstance);
                item.GetDisplayName(SIGDN_FILESYSPATH, out nint name);
                try { return Marshal.PtrToStringUni(name); }
                finally { Marshal.FreeCoTaskMem(name); }
            }
            catch (COMException)
            {
                return null;
            }
        }

        public int StartOperations() => S_OK;
        public int FinishOperations(int hrResult) => S_OK;
        public int PreRenameItem(uint dwFlags, nint psiItem, nint pszNewName) => S_OK;
        public int PostRenameItem(uint dwFlags, nint psiItem, nint pszNewName, int hrRename, nint psiNewlyCreated) => S_OK;
        public int PreMoveItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName) => S_OK;
        public int PostMoveItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName, int hrMove, nint psiNewlyCreated) => S_OK;
        public int PreCopyItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName) => S_OK;
        public int PostCopyItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName, int hrCopy, nint psiNewlyCreated) => S_OK;
        public int PreNewItem(uint dwFlags, nint psiDestinationFolder, nint pszNewName) => S_OK;
        public int PostNewItem(uint dwFlags, nint psiDestinationFolder, nint pszNewName, nint pszTemplateName, uint dwFileAttributes, int hrNew, nint psiNewItem) => S_OK;
        public int UpdateProgress(uint iWorkTotal, uint iWorkSoFar) => S_OK;
        public int ResetTimer() => S_OK;
        public int PauseTimer() => S_OK;
        public int ResumeTimer() => S_OK;
    }
}
