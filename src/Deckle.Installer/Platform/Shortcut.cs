using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Deckle.Installer;

// ── Shortcut ──────────────────────────────────────────────────────────────────
//
// Writes a Start Menu .lnk pointing at the installed Deckle.exe. A .lnk is
// intrinsically a COM artefact (IShellLink + IPersistFile), so there's no pure
// Win32 shortcut. The AOT-safe way to consume that COM is source-generated interop
// ([GeneratedComInterface]) rather than the classic ComImport/coclass activation,
// which NativeAOT doesn't support: we CoCreateInstance the ShellLink ourselves and
// wrap the raw pointer with StrategyBasedComWrappers.
//
// No Desktop shortcut — that's the deliberate modern-Windows stance.
internal static partial class Shortcut
{
    public static void CreateStartMenu(string targetExe, string shortcutName, string? description)
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        Directory.CreateDirectory(programs);
        string lnkPath = Path.Combine(programs, shortcutName + ".lnk");

        // ShellLink is ThreadingModel=Both, so the console's default MTA is fine.
        // S_FALSE (already initialised) is not an error.
        _ = CoInitializeEx(nint.Zero, COINIT_MULTITHREADED);

        int hr = CoCreateInstance(in CLSID_ShellLink, nint.Zero, CLSCTX_INPROC_SERVER, in IID_IShellLinkW, out nint ptr);
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            var cw = new StrategyBasedComWrappers();
            object rcw = cw.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);

            var link = (IShellLinkW)rcw;
            link.SetPath(targetExe);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetExe)!);
            link.SetIconLocation(targetExe, 0);
            if (description is not null) link.SetDescription(description);

            // Cast triggers QueryInterface for IPersistFile on the same object.
            var file = (IPersistFile)rcw;
            file.Save(lnkPath, true);
        }
        finally
        {
            Marshal.Release(ptr); // GetOrCreateObjectForComInstance took its own ref
        }
    }

    public static void RemoveStartMenu(string shortcutName)
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        string lnkPath = Path.Combine(programs, shortcutName + ".lnk");
        if (File.Exists(lnkPath)) File.Delete(lnkPath);
    }

    // ── COM activation ───────────────────────────────────────────────────────────

    private const uint CLSCTX_INPROC_SERVER = 1;
    private const uint COINIT_MULTITHREADED = 0;

    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellLinkW = new("000214F9-0000-0000-C000-000000000046");

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);
}

// IShellLinkW — every method declared in exact vtable order (the generator builds
// the vtable by declaration order). Unused slots keep correct arity with nint
// buffers so the slots we DO call land on the right offsets. void return =
// HRESULT-checked (throws on failure), which is the behaviour we want for Set*.
[GeneratedComInterface]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal partial interface IShellLinkW
{
    void GetPath(nint pszFile, int cch, nint pfd, uint fFlags);
    void GetIDList(out nint ppidl);
    void SetIDList(nint pidl);
    void GetDescription(nint pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory(nint pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments(nint pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out ushort pwHotkey);
    void SetHotkey(ushort wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation(nint pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(nint hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

// IPersistFile — IPersist::GetClassID occupies the first slot.
[GeneratedComInterface]
[Guid("0000010B-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    void GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName);
    void GetCurFile(out nint ppszFileName);
}
