using System.Runtime.InteropServices;
using static Deckle.Shell.Taskbar.TaskbarNativeMethods;

namespace Deckle.Shell.Taskbar;

// Puts the native DLL into explorer.exe, the way TranslucentTB does it, with
// no process scan and no remote thread:
//
//   1. load the DLL here (the file is the versioned copy, see VersionedNativeCopy);
//   2. install a WH_CALLWNDPROC hook, whose procedure is the DLL's exported
//      DeckleTaskbarHookProc, on the thread that owns Shell_TrayWnd;
//   3. send Shell_TrayWnd one registered message so the hook fires: Windows
//      maps the DLL into explorer to run the procedure, and the procedure
//      starts the engine, which pins the DLL itself;
//   4. remove the hook and release our own copy of the DLL.
//
// The injection is one-shot. When explorer restarts the DLL dies with it, and
// the caller injects again on TaskbarCreated.
internal static class TaskbarHookInjector
{
    private const string TaskbarWindowClass = "Shell_TrayWnd";
    private const string HookExport = "DeckleTaskbarHookProc";

    // Must spell what engine.cpp registers.
    internal const string InjectMessageName = "Deckle.Taskbar.Inject.v1";

    public static InjectionResult Inject(string dllPath)
    {
        nint taskbar = FindWindow(TaskbarWindowClass, null);
        if (taskbar == 0) return InjectionResult.Failed("find_taskbar_window", Marshal.GetLastWin32Error());

        uint threadId = GetWindowThreadProcessId(taskbar, out _);
        if (threadId == 0) return InjectionResult.Failed("get_taskbar_thread", Marshal.GetLastWin32Error());

        if (!NativeLibrary.TryLoad(dllPath, out nint module))
            return InjectionResult.Failed("load_dll", Marshal.GetLastWin32Error());

        try
        {
            if (!NativeLibrary.TryGetExport(module, HookExport, out nint procedure))
                return InjectionResult.Failed("find_hook_export", Marshal.GetLastWin32Error());

            uint message = RegisterWindowMessage(InjectMessageName);
            if (message == 0) return InjectionResult.Failed("register_message", Marshal.GetLastWin32Error());

            nint hook = SetWindowsHookEx(WH_CALLWNDPROC, procedure, module, threadId);
            if (hook == 0) return InjectionResult.Failed("install_hook", Marshal.GetLastWin32Error());

            try
            {
                // Bounded: a hung explorer must not hang the caller.
                nint sent = SendMessageTimeout(taskbar, message, 0, 0, SMTO_ABORTIFHUNG | SMTO_BLOCK, 5000, out _);
                if (sent == 0) return InjectionResult.Failed("send_message", Marshal.GetLastWin32Error());
            }
            finally
            {
                UnhookWindowsHookEx(hook);
            }
            return InjectionResult.Success;
        }
        finally
        {
            NativeLibrary.Free(module);
        }
    }
}

// How an injection ended: which step failed and with which Win32 error.
internal readonly record struct InjectionResult(bool Succeeded, string Step, int Win32Error)
{
    public static InjectionResult Success { get; } = new(true, "", 0);

    public static InjectionResult Failed(string step, int win32Error) => new(false, step, win32Error);
}
