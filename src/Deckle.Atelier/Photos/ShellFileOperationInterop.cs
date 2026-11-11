using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Deckle.Atelier;

// The shell's file operation engine, the one File Explorer uses: IFileOperation
// and its progress sink (shobjidl_core.h). Source-generated COM interop, as in
// Deckle.Install, so no classic [ComImport] activation. Every method is
// declared in vtable order; the slots this module never calls take nint so
// the ones it calls land on the right offsets.
internal static partial class ShellFileOperationInterop
{
    public static readonly Guid CLSID_FileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    public static readonly Guid IID_IFileOperation = new("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8");
    public static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    public const uint CLSCTX_INPROC_SERVER = 1;

    // IFileOperation::SetOperationFlags (Microsoft Learn).
    public const uint FOF_SILENT = 0x0004;
    public const uint FOF_NOCONFIRMATION = 0x0010;
    public const uint FOF_ALLOWUNDO = 0x0040;
    public const uint FOF_NOERRORUI = 0x0400;
    public const uint FOF_WANTNUKEWARNING = 0x4000;
    public const uint FOFX_RECYCLEONDELETE = 0x00080000;

    // _TRANSFER_SOURCE_FLAGS, as PreDeleteItem receives it: "Recycle on file
    // delete, if possible."
    public const uint TSF_DELETE_RECYCLE_IF_POSSIBLE = 0x80;

    public const uint SIGDN_FILESYSPATH = 0x80058000;

    // HRESULT_FROM_WIN32(ERROR_CANCELLED).
    public const int HR_CANCELLED = unchecked((int)0x800704C7);
    public const int S_OK = 0;

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHCreateItemFromParsingName(string pszPath, nint pbc, in Guid riid, out nint ppv);
}

[GeneratedComInterface]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
internal partial interface IShellItem
{
    void BindToHandler(nint pbc, in Guid bhid, in Guid riid, out nint ppv);
    void GetParent(out nint ppsi);
    void GetDisplayName(uint sigdnName, out nint ppszName);
    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    void Compare(nint psi, uint hint, out int piOrder);
}

[GeneratedComInterface]
[Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
internal partial interface IFileOperation
{
    void Advise(IFileOperationProgressSink pfops, out uint pdwCookie);
    void Unadvise(uint dwCookie);
    void SetOperationFlags(uint dwOperationFlags);
    void SetProgressMessage(nint pszMessage);
    void SetProgressDialog(nint popd);
    void SetProperties(nint pproparray);
    void SetOwnerWindow(nint hwndOwner);
    void ApplyPropertiesToItem(nint psiItem);
    void ApplyPropertiesToItems(nint punkItems);
    void RenameItem(nint psiItem, nint pszNewName, nint pfopsItem);
    void RenameItems(nint pUnkItems, nint pszNewName);
    void MoveItem(nint psiItem, nint psiDestinationFolder, nint pszNewName, nint pfopsItem);
    void MoveItems(nint punkItems, nint psiDestinationFolder);
    void CopyItem(nint psiItem, nint psiDestinationFolder, nint pszCopyName, nint pfopsItem);
    void CopyItems(nint punkItems, nint psiDestinationFolder);
    void DeleteItem(nint psiItem, nint pfopsItem);
    void DeleteItems(nint punkItems);
    void NewItem(nint psiDestinationFolder, uint dwFileAttributes, nint pszName, nint pszTemplateName, nint pfopsItem);
    [PreserveSig] int PerformOperations();
    void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
}

// Every method returns its HRESULT itself: PreDeleteItem's error is how the
// sink stops a deletion, and the others must answer S_OK.
[GeneratedComInterface]
[Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
internal partial interface IFileOperationProgressSink
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int hrResult);
    [PreserveSig] int PreRenameItem(uint dwFlags, nint psiItem, nint pszNewName);
    [PreserveSig] int PostRenameItem(uint dwFlags, nint psiItem, nint pszNewName, int hrRename, nint psiNewlyCreated);
    [PreserveSig] int PreMoveItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName);
    [PreserveSig] int PostMoveItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName, int hrMove, nint psiNewlyCreated);
    [PreserveSig] int PreCopyItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName);
    [PreserveSig] int PostCopyItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName, int hrCopy, nint psiNewlyCreated);
    [PreserveSig] int PreDeleteItem(uint dwFlags, nint psiItem);
    [PreserveSig] int PostDeleteItem(uint dwFlags, nint psiItem, int hrDelete, nint psiNewlyCreated);
    [PreserveSig] int PreNewItem(uint dwFlags, nint psiDestinationFolder, nint pszNewName);
    [PreserveSig] int PostNewItem(uint dwFlags, nint psiDestinationFolder, nint pszNewName, nint pszTemplateName, uint dwFileAttributes, int hrNew, nint psiNewItem);
    [PreserveSig] int UpdateProgress(uint iWorkTotal, uint iWorkSoFar);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}
