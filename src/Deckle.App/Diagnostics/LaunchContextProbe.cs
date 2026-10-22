using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Deckle.Diagnostics;

namespace Deckle.App;

// Records, once at startup, what decides whether this process may promote its
// windows to the top. SetWindowPos(HWND_TOPMOST) on an existing window, which
// is how the HUD and the other always-on-top WinUI windows get there, needs
// SetForegroundWindow permission. Learn lists when a process has it: among
// others, it was started by the foreground process, there is no foreground
// window, or it received the last input event. How the process was started
// (the launcher, a post-build relaunch, the logon task) changes which of these
// hold, and nothing else in the log says how a session was started.
//
// Ancestry walks the parent chain because the process the shell starts is the
// crash supervisor, so the direct parent of a running Deckle is always
// Deckle.exe. Only image file names are kept: a full path can carry the
// user's name.
internal static class LaunchContextProbe
{
    private const int MaxAncestors = 4;

    internal static void Emit()
    {
        if (!DeckleAppSource.Log.IsEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;

        try
        {
            var ancestors = ReadAncestors();
            string ancestry = ancestors.Count == 0
                ? "?"
                : string.Join(" < ", ancestors.Select(a => a.Image));

            string foregroundImage = "";
            string foregroundClass = "";
            bool foregroundIsAncestor = false;
            IntPtr foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero)
            {
                GetWindowThreadProcessId(foreground, out uint foregroundPid);
                foregroundImage = ReadImageName(foregroundPid) ?? "?";
                var sb = new StringBuilder(128);
                foregroundClass = GetClassName(foreground, sb, sb.Capacity) > 0 ? sb.ToString() : "";
                foregroundIsAncestor = foregroundPid == (uint)Environment.ProcessId
                    || ancestors.Any(a => a.Pid == foregroundPid);
            }

            int lockTimeoutMs = SystemParametersInfo(
                SPI_GETFOREGROUNDLOCKTIMEOUT, 0, out uint timeout, 0) ? (int)timeout : -1;

            DeckleAppSource.Log.LaunchContextDetail(
                ancestry, IsElevated(),
                foregroundImage, foregroundClass,
                foregroundIsAncestor, lockTimeoutMs);
        }
        catch
        {
            // A diagnostic read never fails the boot.
        }
    }

    private static bool IsElevated()
    {
        // With UAC, an administrator's filtered token does not hold the
        // Administrators group, so this reads true only when elevated.
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    // Stops at the first parent that has exited, cannot be opened, or was
    // created after its child: Windows reuses process ids, so a later process
    // under the same id is not the parent.
    private static List<(uint Pid, string Image)> ReadAncestors()
    {
        var chain = new List<(uint, string)>();
        uint pid = (uint)Environment.ProcessId;
        long childCreated = long.MaxValue;
        for (int i = 0; i < MaxAncestors; i++)
        {
            if (!TryReadParent(pid, out uint parentPid, out long created)) break;
            if (created > childCreated) break;
            childCreated = created;
            if (parentPid == 0) break;

            if (!TryReadCreation(parentPid, out long parentCreated)
                || parentCreated > childCreated)
            {
                chain.Add((parentPid, "?"));
                break;
            }
            chain.Add((parentPid, ReadImageName(parentPid) ?? "?"));
            pid = parentPid;
        }
        return chain;
    }

    private static bool TryReadParent(uint pid, out uint parentPid, out long created)
    {
        parentPid = 0;
        created = 0;
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return false;
        try
        {
            if (!GetProcessTimes(process, out created, out _, out _, out _)) return false;
            var info = new PROCESS_BASIC_INFORMATION();
            int status = NtQueryInformationProcess(
                process, ProcessBasicInformation, ref info,
                Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _);
            if (status != 0) return false;
            parentPid = (uint)info.InheritedFromUniqueProcessId.ToInt64();
            return true;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool TryReadCreation(uint pid, out long created)
    {
        created = 0;
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return false;
        try { return GetProcessTimes(process, out created, out _, out _, out _); }
        finally { CloseHandle(process); }
    }

    private static string? ReadImageName(uint pid)
    {
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            return QueryFullProcessImageNameW(process, 0, buffer, ref size)
                ? Path.GetFileName(buffer.ToString())
                : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int ProcessBasicInformation = 0;
    private const uint SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(
        IntPtr hProcess, out long lpCreationTime, out long lpExitTime,
        out long lpKernelTime, out long lpUserTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(
        IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(
        uint uiAction, uint uiParam, out uint pvParam, uint fWinIni);
}
