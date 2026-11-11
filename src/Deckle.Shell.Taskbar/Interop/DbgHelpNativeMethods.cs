using System.Runtime.InteropServices;

namespace Deckle.Shell.Taskbar;

// The symbol API Windows itself ships in System32, enough to read an address out
// of a PDB file already on disk. That copy cannot fetch from a symbol server
// (no symsrv beside it), which Deckle does not need: ExplorerFunctionLocator
// downloads the PDB itself and hands dbghelp the file. Single-threaded by
// contract, so every call goes under one lock.
// https://learn.microsoft.com/windows/win32/debug/dbghelp-functions
internal static partial class DbgHelpNativeMethods
{
    internal const uint SYMOPT_FAIL_CRITICAL_ERRORS = 0x00000200;
    internal const uint SYMOPT_EXACT_SYMBOLS = 0x00000400;

    // SYMBOL_INFOW up to its Name: Address at 56, MaxNameLen at 80, 88 bytes in all.
    internal const int SymbolInfoSize = 88;
    internal const int SymbolInfoAddressOffset = 56;
    internal const int SymbolInfoMaxNameLenOffset = 80;

    [LibraryImport("dbghelp.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint SymSetOptions(uint options);

    [LibraryImport("dbghelp.dll", EntryPoint = "SymInitializeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SymInitialize(nint process, string? searchPath, [MarshalAs(UnmanagedType.Bool)] bool invadeProcess);

    [LibraryImport("dbghelp.dll", EntryPoint = "SymLoadModuleExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial ulong SymLoadModuleEx(
        nint process, nint file, string imageName, string? moduleName, ulong baseOfDll, uint dllSize, nint data, uint flags);

    [LibraryImport("dbghelp.dll", EntryPoint = "SymFromNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SymFromName(nint process, string name, nint symbol);

    [LibraryImport("dbghelp.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SymCleanup(nint process);
}
