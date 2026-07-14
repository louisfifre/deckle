using Deckle.Core;

namespace Deckle.Shell;

// Registers the host-selected global hotkeys and intercepts WM_HOTKEY via SetWindowSubclass.
// SetWindowSubclass chains into the existing message pump of the host window
// (message-only window in our case) without replacing its WndProc — the only
// safe approach.
//
// Layout portability: the three chords all use the physical key to the left
// of "1" (scancode 0x29). At registration time we resolve the current VK for
// that scancode via MapVirtualKeyExW(GetKeyboardLayout(0)). On layout switch
// we receive WM_INPUTLANGCHANGE, unregister, re-resolve, re-register.
public sealed class HotkeyManager : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly Action<int> _onHotkey;

    // The delegate must live in a field to prevent the GC from collecting it
    // while the native code holds the function pointer.
    private NativeMethods.SubclassProc? _subclassDelegate;

    private bool _disposed;
    private readonly GlobalHotkeyBindings _bindings;

    // Arbitrary identifier to retrieve our subclass at Remove time.
    private static readonly UIntPtr SubclassId = new(0x5748_4B45); // "WHKE"

    // (id, modifiers) pairs for RegisterAll / UnregisterAll — the full
    // catalogue of chords the shell knows how to bind. Adding a 4th hotkey
    // is just adding a line here.
    private static readonly GlobalHotkeyBinding[] Catalogue =
    {
        new(NativeMethods.HOTKEY_ID_TRANSCRIBE,
            NativeMethods.MOD_WIN | NativeMethods.MOD_NOREPEAT),
        new(NativeMethods.HOTKEY_ID_PRIMARY_REWRITE,
            NativeMethods.MOD_SHIFT | NativeMethods.MOD_WIN | NativeMethods.MOD_NOREPEAT),
        new(NativeMethods.HOTKEY_ID_SECONDARY_REWRITE,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_WIN | NativeMethods.MOD_NOREPEAT),
    };

    // The subset of the catalogue this instance actually binds. The host
    // decides which chords exist — an absent module's hotkeys are not
    // registered at all, leaving the chord free for other apps — the shell
    // only knows how to bind them.
    public HotkeyManager(IntPtr hwnd, Action<int> onHotkey, IReadOnlyCollection<int> hotkeyIds)
    {
        _hwnd = hwnd;
        _onHotkey = onHotkey;
        GlobalHotkeyBinding[] selected = Array.FindAll(
            Catalogue, binding => hotkeyIds.Contains(binding.Id));
        _bindings = new GlobalHotkeyBindings(hwnd, selected, new Win32GlobalHotkeyApi());
    }

    public void Register()
    {
        _subclassDelegate = SubclassCallback;
        if (!NativeMethods.SetWindowSubclass(_hwnd, _subclassDelegate, SubclassId, IntPtr.Zero))
            throw new InvalidOperationException("The hotkey window subclass could not be installed.");

        try { RegisterAll(); }
        catch
        {
            if (NativeMethods.RemoveWindowSubclass(_hwnd, _subclassDelegate, SubclassId))
                _subclassDelegate = null;
            throw;
        }
    }

    // Resolves the current VK for the physical "left of 1" key under the
    // active keyboard layout, unregisters any previous bindings, and
    // registers the selected chords. Called at startup and on every WM_INPUTLANGCHANGE.
    private void RegisterAll()
    {
        // Always unregister first — no-op if nothing is registered yet, but
        // required when re-registering after a layout change.
        _bindings.Unregister();

        IntPtr hkl = NativeMethods.GetKeyboardLayout(0);
        uint vk = NativeMethods.MapVirtualKeyExW(
            NativeMethods.SC_LEFT_OF_ONE,
            NativeMethods.MAPVK_VSC_TO_VK_EX,
            hkl);

        if (vk == 0)
        {
            DeckleShellSource.Log.HotkeyVkResolveFailed();
            DeckleShellSource.Log.HotkeyVkResolveFailedDetail(hkl.ToInt64());
            return;
        }

        DeckleShellSource.Log.HotkeyRegistered(vk, hkl.ToInt64());

        _bindings.Register(vk);
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
            try { RegisterAll(); }
            catch (Exception ex)
            {
                DeckleShellSource.Log.HotkeyReregisterFailed();
                DeckleShellSource.Log.HotkeyReregisterFailedDetail(ex.Message);
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
