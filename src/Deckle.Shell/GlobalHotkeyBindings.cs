using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Shell;

// One OS-wide chord the host asks for. Modifiers are the RegisterHotKey
// fsModifiers (MOD_* flags, MOD_NOREPEAT included); ScanCode names the
// physical key, 0xE0-prefixed in the high byte for an extended key.
public readonly record struct GlobalHotkeyBinding(int Id, uint Modifiers, uint ScanCode);

// A chord the shell could not acquire. Win32Error is GetLastError after
// RegisterHotKey (1409 = ERROR_HOTKEY_ALREADY_REGISTERED: another app, or
// another Deckle binding, owns the chord), the error of the failed release
// when the id is still held from the previous set, or 0 when the scancode has
// no virtual key under the active keyboard layout.
public sealed record HotkeyRegistrationFailure(int Id, int Win32Error);

internal interface IGlobalHotkeyApi
{
    bool Register(IntPtr window, int id, uint modifiers, uint virtualKey);
    bool Unregister(IntPtr window, int id);
    int LastError { get; }
}

// Acquires a set of OS-wide chords one by one. A chord that fails is reported
// and skipped; the others stay registered. Each call replaces the previous
// set: everything acquired before is released first. A release that fails
// keeps its id tracked, so the next Unregister retries it and the next
// Register reports that id instead of stacking a second registration on it.
//
// The virtual key is resolved per binding through a delegate rather than an
// HKL: the host resolves it with MapVirtualKeyExW under the active layout,
// and a test passes a plain mapping, so the class stays free of Win32 calls
// beyond IGlobalHotkeyApi.
//
// onRelease (id, released, win32Error) reports each release attempt so the
// owner can log it; this class stays free of any provider.
internal sealed class GlobalHotkeyBindings(
    IntPtr window, IGlobalHotkeyApi api, Action<int, bool, int>? onRelease = null)
{
    private readonly List<int> _acquired = [];
    private readonly Dictionary<int, int> _releaseErrors = [];

    public IReadOnlyList<HotkeyRegistrationFailure> Register(
        IReadOnlyList<GlobalHotkeyBinding> requested,
        Func<uint, uint> resolveVirtualKey)
    {
        Unregister();

        var failures = new List<HotkeyRegistrationFailure>();
        foreach (GlobalHotkeyBinding binding in requested)
        {
            if (_acquired.Contains(binding.Id))
            {
                failures.Add(new(binding.Id, _releaseErrors.GetValueOrDefault(binding.Id)));
                continue;
            }

            uint virtualKey = resolveVirtualKey(binding.ScanCode);
            if (virtualKey == 0)
            {
                failures.Add(new(binding.Id, 0));
                continue;
            }

            if (api.Register(window, binding.Id, binding.Modifiers, virtualKey))
                _acquired.Add(binding.Id);
            else
                failures.Add(new(binding.Id, api.LastError));
        }
        return failures;
    }

    public bool Unregister()
    {
        for (int index = _acquired.Count - 1; index >= 0; index--)
        {
            int id = _acquired[index];
            if (api.Unregister(window, id))
            {
                _acquired.RemoveAt(index);
                _releaseErrors.Remove(id);
                onRelease?.Invoke(id, true, 0);
            }
            else
            {
                // Read before the callback: its logging can overwrite the last error.
                int error = api.LastError;
                _releaseErrors[id] = error;
                onRelease?.Invoke(id, false, error);
            }
        }
        return _acquired.Count == 0;
    }
}

internal sealed class Win32GlobalHotkeyApi : IGlobalHotkeyApi
{
    public bool Register(IntPtr window, int id, uint modifiers, uint virtualKey) =>
        NativeMethods.RegisterHotKey(window, id, modifiers, virtualKey);

    public bool Unregister(IntPtr window, int id) =>
        NativeMethods.UnregisterHotKey(window, id);

    public int LastError => Marshal.GetLastWin32Error();
}
