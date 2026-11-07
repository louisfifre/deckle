using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Deckle.Shell.Taskbar;

// The Win32 calls the taskbar module makes from its own process. Nothing here
// reaches into explorer: these are the registry APIs the managed
// Microsoft.Win32.Registry wrapper does not expose.
internal static partial class TaskbarNativeMethods
{
    // A subkey was added or deleted under the watched key.
    internal const uint REG_NOTIFY_CHANGE_NAME = 0x00000001;

    // A value was added, changed or deleted (the key's last write time moved).
    internal const uint REG_NOTIFY_CHANGE_LAST_SET = 0x00000004;

    internal const int ERROR_SUCCESS = 0;

    // Registers one notification: the event is signalled once, at the next
    // change matching the filter, and the call must be made again for the next
    // one. Asynchronous with an event, the registration lives as long as the
    // key handle and the calling thread, which is why the watcher arms it from
    // its own dedicated thread.
    // https://learn.microsoft.com/windows/win32/api/winreg/nf-winreg-regnotifychangekeyvalue
    [LibraryImport("advapi32.dll")]
    internal static partial int RegNotifyChangeKeyValue(
        SafeRegistryHandle hKey,
        [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
        uint dwNotifyFilter,
        SafeWaitHandle hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);
}
