using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Shell;

// Binds the host's global hotkeys and intercepts WM_HOTKEY through
// SetWindowSubclass, which chains into the host window's pump without
// replacing its WndProc. The host decides which chords exist and what they do;
// the shell only binds them.
//
// Every binding names a physical key by scancode. Each registration resolves
// the current VK through MapVirtualKeyExW(GetKeyboardLayout(0)), and
// WM_INPUTLANGCHANGE re-resolves and re-registers the current set.
//
// While the screen saver shows, the set moves to its desktop through
// ScreenSaverHotkeyRelay and comes back when it ends; HotkeyDesktopPolicy
// says why and when. The desktop switch is read from a WinEvent hook on this
// same thread, so the move never races a rebind.
public sealed class HotkeyManager : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly Action<int> _onHotkey;

    // The delegate must live in a field to prevent the GC from collecting it
    // while the native code holds the function pointer.
    private NativeMethods.SubclassProc? _subclassDelegate;
    private ScreenSaverNativeMethods.WinEventProc? _desktopSwitchDelegate;
    private IntPtr _desktopSwitchHook;

    // Non-null while the set is held on the screen saver's desktop.
    private ScreenSaverHotkeyRelay? _relay;

    // A press relayed from the screen saver's desktop, carrying the hotkey id
    // in wParam. Registered by name so it cannot collide with another
    // subclass of the host window.
    private static readonly uint RelayedHotkeyMessage =
        ScreenSaverNativeMethods.RegisterWindowMessage("Deckle.ScreenSaverHotkey");

    private bool _disposed;
    private readonly GlobalHotkeyBindings _bindings;
    private IReadOnlyList<GlobalHotkeyBinding> _requested;

    private static readonly UIntPtr SubclassId = new(0x5748_4B45); // "WHKE"

    public HotkeyManager(IntPtr hwnd, Action<int> onHotkey, IReadOnlyList<GlobalHotkeyBinding> bindings)
    {
        _hwnd = hwnd;
        _onHotkey = onHotkey;
        _requested = bindings;
        _bindings = new GlobalHotkeyBindings(hwnd, new Win32GlobalHotkeyApi(), OnRelease);
    }

    private static void OnRelease(int id, bool released, int win32Error)
    {
        if (released)
            DeckleShellSource.Log.HotkeyUnregistered(id);
        else
            DeckleShellSource.Log.HotkeyReleaseFailed(id, win32Error);
    }

    // The set currently requested (acquired or not).
    public IReadOnlyList<GlobalHotkeyBinding> Bindings => _requested;

    public IReadOnlyList<HotkeyRegistrationFailure> LastFailures { get; private set; } = [];

    // Installs the WM_HOTKEY subclass, then registers the requested set.
    // Throws only when the subclass cannot be installed; a chord that cannot
    // be acquired is returned as a failure.
    public IReadOnlyList<HotkeyRegistrationFailure> Register()
    {
        _subclassDelegate = SubclassCallback;
        if (!NativeMethods.SetWindowSubclass(_hwnd, _subclassDelegate, SubclassId, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            _subclassDelegate = null;
            DeckleShellSource.Log.HotkeySubclassFailed(error);
            throw new InvalidOperationException("The hotkey window subclass could not be installed.");
        }

        // Without the hook the hotkeys keep working everywhere but over the
        // screen saver, as before it existed.
        _desktopSwitchDelegate = OnDesktopSwitch;
        _desktopSwitchHook = ScreenSaverNativeMethods.SetWinEventHook(
            ScreenSaverNativeMethods.EVENT_SYSTEM_DESKTOPSWITCH, ScreenSaverNativeMethods.EVENT_SYSTEM_DESKTOPSWITCH,
            IntPtr.Zero, _desktopSwitchDelegate, 0, 0, ScreenSaverNativeMethods.WINEVENT_OUTOFCONTEXT);
        if (_desktopSwitchHook == IntPtr.Zero)
        {
            _desktopSwitchDelegate = null;
            DeckleShellSource.Log.ScreenSaverHotkeysFailed("desktop_switch_hook", Marshal.GetLastWin32Error());
        }

        RegisterAll();
        if (_desktopSwitchHook != IntPtr.Zero) FollowInputDesktop();
        return LastFailures;
    }

    // Replaces the requested set: releases the current chords, swaps, and
    // acquires the new ones. Same thread as Register (the host window's).
    public IReadOnlyList<HotkeyRegistrationFailure> Rebind(IReadOnlyList<GlobalHotkeyBinding> bindings)
    {
        _requested = bindings;
        return RegisterAll();
    }

    // GlobalHotkeyBindings releases the previous set first. While the screen
    // saver shows, the new set goes to its desktop instead.
    private IReadOnlyList<HotkeyRegistrationFailure> RegisterAll()
    {
        if (_relay is not null)
        {
            StopRelay("rebind");
            // A relay that cannot start has already taken the set back on
            // Default, through this same method.
            return LastFailures = StartRelay() ?? LastFailures;
        }

        IntPtr hkl = NativeMethods.GetKeyboardLayout(0);
        var resolved = new Dictionary<uint, uint>();

        uint Resolve(uint scanCode)
        {
            // MAPVK_VSC_TO_VK_EX takes an extended scancode as is (0xE0 in
            // the high byte) and tells left from right modifiers.
            uint vk = NativeMethods.MapVirtualKeyExW(scanCode, NativeMethods.MAPVK_VSC_TO_VK_EX, hkl);
            if (vk == 0)
            {
                DeckleShellSource.Log.HotkeyVkResolveFailed(scanCode, hkl.ToInt64());
            }
            resolved[scanCode] = vk;
            return vk;
        }

        IReadOnlyList<HotkeyRegistrationFailure> failures = _bindings.Register(_requested, Resolve);

        foreach (GlobalHotkeyBinding binding in _requested)
        {
            if (failures.Any(failure => failure.Id == binding.Id)) continue;
            DeckleShellSource.Log.HotkeyRegistered(
                binding.Id, binding.ScanCode, resolved[binding.ScanCode], hkl.ToInt64());
        }

        LastFailures = failures;
        return failures;
    }

    private IntPtr SubclassCallback(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == NativeMethods.WM_HOTKEY)
        {
            // The callback runs on the UI thread of the host (same pump as
            // DispatcherQueue) — direct call without BeginInvoke / TryEnqueue.
            _onHotkey(wParam.ToInt32());
            return IntPtr.Zero;
        }

        if (uMsg == RelayedHotkeyMessage && RelayedHotkeyMessage != 0)
        {
            // Posted by the relay thread; already back on this thread.
            _onHotkey(wParam.ToInt32());
            return IntPtr.Zero;
        }

        if (uMsg == NativeMethods.WM_INPUTLANGCHANGE)
        {
            // Keyboard layout changed — re-resolve and re-register. Continue
            // chaining so other subclasses / DefWindowProc still see the message.
            DeckleShellSource.Log.HotkeyLayoutChange();
            IReadOnlyList<HotkeyRegistrationFailure> failures = RegisterAll();
            if (failures.Count > 0)
            {
                DeckleShellSource.Log.HotkeyReregisterFailed(failures.Count, failures[0].Win32Error);
                DeckleShellSource.Log.HotkeyReregisterFailedDetail(string.Join(
                    ", ", failures.Select(failure => $"id={failure.Id} err={failure.Win32Error}")));
            }
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // ── Screen saver desktop ──────────────────────────────────────────────

    // The input desktop changed: the screen saver started or ended, or the
    // session locked or unlocked.
    private void OnDesktopSwitch(
        IntPtr hWinEventHook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (!_disposed) FollowInputDesktop();
    }

    // Also run once at Register: Deckle can start while the screen saver
    // already shows, and no switch would then come until it ends.
    private void FollowInputDesktop()
    {
        string desktop = InputDesktopName();
        bool secure = ScreenSaverSecure();
        bool follow = HotkeyDesktopPolicy.Follows(desktop, secure);
        DeckleShellSource.Log.InputDesktopSwitched(desktop, secure, follow);
        if (follow == _relay is not null) return;

        if (follow)
        {
            if (StartRelay() is { } failures) LastFailures = failures;
        }
        else
        {
            StopRelay("screen_saver_ended");
            RegisterAll();
        }
    }

    // Releases the set on Default and takes it on the screen saver's
    // desktop. Null when the relay could not start: the set is then taken
    // back on Default, where it works everywhere but over the screen saver.
    private IReadOnlyList<HotkeyRegistrationFailure>? StartRelay()
    {
        _bindings.Unregister();

        IntPtr hkl = NativeMethods.GetKeyboardLayout(0);
        var chords = _requested
            .Select(b => new ScreenSaverHotkeyRelay.Chord(
                b.Id, b.Modifiers,
                NativeMethods.MapVirtualKeyExW(b.ScanCode, NativeMethods.MAPVK_VSC_TO_VK_EX, hkl)))
            .ToList();

        var relay = new ScreenSaverHotkeyRelay(_hwnd, RelayedHotkeyMessage);
        ScreenSaverHotkeyRelay.StartResult result = relay.Start(chords);
        if (!result.Started)
        {
            DeckleShellSource.Log.ScreenSaverHotkeysFailed(result.FailedStep, result.Win32Error);
            RegisterAll();
            return null;
        }

        _relay = relay;
        DeckleShellSource.Log.ScreenSaverHotkeysFollowed(
            (int)relay.ThreadId, chords.Count - result.Failures.Count, result.Failures.Count);
        return result.Failures;
    }

    private void StopRelay(string reason)
    {
        if (_relay is null) return;
        uint threadId = _relay.ThreadId;
        _relay.Dispose();
        // The screen saver is gone: the cursor goes back where the person
        // left it, so the nudge that closed it leaves no trace.
        if (reason == "screen_saver_ended" && _relay.NudgeOrigin is { } origin)
            ScreenSaverNativeMethods.SetCursorPos(origin.X, origin.Y);
        _relay = null;
        DeckleShellSource.Log.ScreenSaverHotkeysReturned((int)threadId, reason);
    }

    // "" when the input desktop cannot be read: then nothing is followed.
    private static string InputDesktopName()
    {
        IntPtr desktop = ScreenSaverNativeMethods.OpenInputDesktop(0, false, ScreenSaverNativeMethods.DESKTOP_READOBJECTS);
        if (desktop == IntPtr.Zero) return "";
        try
        {
            var name = new System.Text.StringBuilder(64);
            return ScreenSaverNativeMethods.GetUserObjectInformation(
                       desktop, ScreenSaverNativeMethods.UOI_NAME, name, name.Capacity * sizeof(char), out _)
                ? name.ToString()
                : "";
        }
        finally
        {
            ScreenSaverNativeMethods.CloseDesktop(desktop);
        }
    }

    // Read from the setting itself, the policy first. SPI_GETSCREENSAVESECURE
    // was tried and answered "secure" for a non-secure screen saver in two
    // runs out of three while it showed (measured). A value that cannot be
    // read counts as secure: the relay then stays off.
    private static bool ScreenSaverSecure()
    {
        try
        {
            object? value =
                Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\Control Panel\Desktop", "ScreenSaverIsSecure", null)
                ?? Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Control Panel\Desktop", "ScreenSaverIsSecure", "0");
            return value?.ToString() != "0";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_desktopSwitchHook != IntPtr.Zero)
        {
            ScreenSaverNativeMethods.UnhookWinEvent(_desktopSwitchHook);
            _desktopSwitchHook = IntPtr.Zero;
        }
        StopRelay("dispose");

        _bindings.Unregister();

        if (_subclassDelegate is not null)
        {
            if (NativeMethods.RemoveWindowSubclass(_hwnd, _subclassDelegate, SubclassId))
                _subclassDelegate = null;
        }
    }
}
