using System.Runtime.InteropServices;

namespace Deckle.Orchestration;

// Win32 process access the module needs: read a process's image path without
// the rights Process.MainModule demands (which fails across bitness and on
// elevated processes).
internal static class ProcessNativeMethods
{
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint SYNCHRONIZE = 0x00100000;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageNameW(
        nint hProcess, uint dwFlags, [Out] char[] lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint hObject);

    // Full Win32 path of the process image, or null when the process is gone
    // or protected (anti-cheat, some system processes).
    internal static string? TryGetImagePath(int pid)
    {
        nint handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0) return null;
        try
        {
            var buffer = new char[1024];
            uint size = (uint)buffer.Length;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size)
                ? new string(buffer, 0, (int)size)
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
