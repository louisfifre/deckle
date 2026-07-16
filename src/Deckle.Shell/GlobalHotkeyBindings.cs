using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Shell;

internal readonly record struct GlobalHotkeyBinding(int Id, uint Modifiers);

internal interface IGlobalHotkeyApi
{
    bool Register(IntPtr window, int id, uint modifiers, uint virtualKey);
    bool Unregister(IntPtr window, int id);
    int LastError { get; }
}

// Acquires a selected set of OS-wide chords as one transaction. If any chord
// is unavailable, every chord acquired earlier in the attempt is released.
internal sealed class GlobalHotkeyBindings(
    IntPtr window,
    IReadOnlyList<GlobalHotkeyBinding> requested,
    IGlobalHotkeyApi api)
{
    private readonly List<GlobalHotkeyBinding> _acquired = [];

    public void Register(uint virtualKey)
    {
        if (!Unregister())
            throw new InvalidOperationException("A previous global hotkey binding could not be released.");

        foreach (GlobalHotkeyBinding binding in requested)
        {
            if (api.Register(window, binding.Id, binding.Modifiers, virtualKey))
            {
                _acquired.Add(binding);
                continue;
            }

            int error = api.LastError;
            bool rollbackComplete = Unregister();
            throw new InvalidOperationException(
                $"RegisterHotKey id={binding.Id} modifiers=0x{binding.Modifiers:X} " +
                $"vk=0x{virtualKey:X2} failed (Win32 err {error})" +
                (rollbackComplete ? "." : "; an earlier binding could not be released."));
        }
    }

    public bool Unregister()
    {
        for (int index = _acquired.Count - 1; index >= 0; index--)
        {
            if (api.Unregister(window, _acquired[index].Id))
                _acquired.RemoveAt(index);
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

public static class HotkeySelection
{
    public static IReadOnlyList<int> ForModulePresence(
        bool transcriptionPresent,
        bool rewritePresent)
    {
        if (!transcriptionPresent)
            return [];

        if (!rewritePresent)
            return [NativeMethods.HOTKEY_ID_TRANSCRIBE];

        return
        [
            NativeMethods.HOTKEY_ID_TRANSCRIBE,
            NativeMethods.HOTKEY_ID_PRIMARY_REWRITE,
            NativeMethods.HOTKEY_ID_SECONDARY_REWRITE,
        ];
    }
}
