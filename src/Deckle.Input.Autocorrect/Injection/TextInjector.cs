using System.Runtime.InteropServices;
using Deckle.Core.Interop;
using Deckle.Input.Interop;

namespace Deckle.Input.Autocorrect.Injection;

// Replays a correction as synthetic keystrokes via SendInput: a burst of
// Backspaces to erase the divergent tail, then the new suffix typed as Unicode
// scancodes. The whole burst goes out in ONE SendInput call — atomicity is the
// anti-interleave defense: nothing the user types can slip between our
// keystrokes mid-correction. Every event is stamped with SendInputInterop's
// InjectionTag in dwExtraInfo for downstream consumers; the keyboard host
// actually filters our synthesis by the Raw Input hDevice==0 signature of
// SendInput events — the tag is not read back on the receive side today.
public sealed class TextInjector : ITextInjector
{
    // INPUT.type for a keyboard event (INPUT_KEYBOARD).
    private const uint INPUT_KEYBOARD = 1;

    // ki_dwFlags bits. UNICODE: ki_wScan carries the UTF-16 code unit, ki_wVk
    // must be 0. KEYUP: the matching release of a press.
    private const uint KEYEVENTF_KEYUP   = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const ushort VK_BACK = 0x08;

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    // Corrects `current` into `target` by the minimal diff. No-op (identical, or
    // pure prefix growth handled by TypeText callers) returns true without
    // touching the input stream. Returns false only on a partial/failed
    // SendInput.
    public bool Replace(string current, string target)
    {
        var plan = InjectionPlan.Compute(current, target);
        if (plan.IsNoOp) return true;

        // Two events (down+up) per backspace and per UTF-16 code unit of Text.
        var inputs = new INPUT[(plan.Backspaces + plan.Text.Length) * 2];
        int i = 0;

        for (int b = 0; b < plan.Backspaces; b++)
        {
            inputs[i++] = KeyEvent(VK_BACK, 0, 0);
            inputs[i++] = KeyEvent(VK_BACK, 0, KEYEVENTF_KEYUP);
        }

        foreach (char unit in plan.Text)
        {
            inputs[i++] = KeyEvent(0, unit, KEYEVENTF_UNICODE);
            inputs[i++] = KeyEvent(0, unit, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
        }

        return Send(inputs);
    }

    // Injects bare text (no preceding backspaces) — the CLI `inject` command.
    // Same Unicode mechanics as Replace's suffix.
    public bool TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;

        var inputs = new INPUT[text.Length * 2];
        int i = 0;
        foreach (char unit in text)
        {
            inputs[i++] = KeyEvent(0, unit, KEYEVENTF_UNICODE);
            inputs[i++] = KeyEvent(0, unit, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
        }

        return Send(inputs);
    }

    private static INPUT KeyEvent(ushort vk, ushort scan, uint flags) => new()
    {
        type           = INPUT_KEYBOARD,
        ki_wVk         = vk,
        ki_wScan       = scan,
        ki_dwFlags     = flags,
        ki_dwExtraInfo = SendInputInterop.InjectionTag,
    };

    private static bool Send(INPUT[] inputs)
    {
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);
        return sent == inputs.Length;
    }
}
