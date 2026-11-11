using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Input;

namespace Deckle.Shell.Taskbar;

// Presses the two key sequences the Windows key tracker needs, through
// SendInput. Each sequence goes out as one call, so nothing the person types can
// land inside it. Every event carries Deckle's injection tag in dwExtraInfo.
//
// The throwaway key is virtual key 0xFF, which no key uses: it does nothing
// where it lands, yet its press between the Windows key's down and up is
// enough for Windows to stop treating that press as a lone tap, the way
// PowerToys' Keyboard Manager does it.
//
// Windows + T is Windows' own shortcut for the taskbar. Pressed from here it
// reaches the shell like a typed one, and the shell reveals the taskbar and puts
// it above the other windows.
public sealed class WindowsKeyInjector : IWindowsKeyInjector
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;

    private const ushort VkLeftWindows = 0x5B;
    private const ushort VkT = 0x54;
    private const ushort VkThrowaway = 0xFF;

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public int LastWin32Error { get; private set; }

    public bool SuppressStart() => Send(
    [
        Key(VkThrowaway, 0),
        Key(VkThrowaway, KeyEventKeyUp),
    ]);

    public bool FocusTaskbar() => Send(
    [
        Key(VkLeftWindows, KeyEventExtendedKey),
        Key(VkT, 0),
        Key(VkT, KeyEventKeyUp),
        Key(VkLeftWindows, KeyEventExtendedKey | KeyEventKeyUp),
    ]);

    private static INPUT Key(ushort virtualKey, uint flags) => new()
    {
        type = InputKeyboard,
        ki_wVk = virtualKey,
        ki_dwFlags = flags,
        ki_dwExtraInfo = SendInputInterop.InjectionTag,
    };

    private bool Send(INPUT[] inputs)
    {
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);
        // A UIPI block returns 0 and does not always set an error code, so 0
        // here means "blocked without a reason", not "no failure".
        LastWin32Error = sent == inputs.Length ? 0 : Marshal.GetLastPInvokeError();
        return sent == inputs.Length;
    }
}
