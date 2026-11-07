using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Deckle.Core;

public static partial class NativeMethods
{
    // The microphone is captured through WASAPI (Deckle.Audio's WasapiInterop),
    // so only the waveOut render side is declared here.

    // ── waveOut (render audio PCM) ──────────────────────────────────────────

    [DllImport("winmm.dll")]
    public static extern uint waveOutOpen(
        out IntPtr phwo, uint uDeviceID,
        ref WAVEFORMATEX pwfx,
        IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

    [DllImport("winmm.dll")]
    public static extern uint waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);

    [DllImport("winmm.dll")]
    public static extern uint waveOutWrite(IntPtr hwo, IntPtr pwh, uint cbwh);

    [DllImport("winmm.dll")]
    public static extern uint waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);

    [DllImport("winmm.dll")]
    public static extern uint waveOutReset(IntPtr hwo);

    [DllImport("winmm.dll")]
    public static extern uint waveOutClose(IntPtr hwo);

    // ── kernel32 (event, handle) ─────────────────────────────────────────────

    [DllImport("kernel32.dll")]
    public static extern IntPtr CreateEvent(
        IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr hObject);

}
