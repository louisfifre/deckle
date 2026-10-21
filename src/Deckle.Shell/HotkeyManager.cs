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
public sealed class HotkeyManager : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly Action<int> _onHotkey;

    // The delegate must live in a field to prevent the GC from collecting it
    // while the native code holds the function pointer.
    private NativeMethods.SubclassProc? _subclassDelegate;

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

        return RegisterAll();
    }

    // Replaces the requested set: releases the current chords, swaps, and
    // acquires the new ones. Same thread as Register (the host window's).
    public IReadOnlyList<HotkeyRegistrationFailure> Rebind(IReadOnlyList<GlobalHotkeyBinding> bindings)
    {
        _requested = bindings;
        return RegisterAll();
    }

    // GlobalHotkeyBindings releases the previous set first.
    private IReadOnlyList<HotkeyRegistrationFailure> RegisterAll()
    {
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _bindings.Unregister();

        if (_subclassDelegate is not null)
        {
            if (NativeMethods.RemoveWindowSubclass(_hwnd, _subclassDelegate, SubclassId))
                _subclassDelegate = null;
        }
    }
}
