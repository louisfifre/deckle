using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Audio.Internal;

// The MME device a capture or a playback actually ran on, as the log names it.
// DeviceId is the waveIn/waveOut index, -1 when even the wave mapper could not
// say which device it resolves to.
internal readonly record struct WaveDeviceIdentity(int DeviceId, string Name, bool IsDefault)
{
    public const string UnknownName = "unknown";

    // Used when no listener is attached: resolving the name costs driver calls
    // that only the log needs.
    public static WaveDeviceIdentity Unresolved(int requestedDeviceId)
        => new(requestedDeviceId, UnknownName, requestedDeviceId < 0);
}

// winmm and kernel32 entry points the observability of the audio module needs
// and that Deckle.Core.NativeMethods does not declare. Kept module-local so the
// device identity and the native error names stay beside the events that carry
// them.
internal static class WaveDeviceInterop
{
    public const uint WAVE_MAPPER = 0xFFFFFFFF;

    // DRVM_MAPPER (0x2000) + 21, mmddk.h: asks the wave mapper which device it
    // currently resolves to, the system default device.
    private const uint DRVM_MAPPER_PREFERRED_GET = 0x2015;

    // (HWAVEIN)WAVE_MAPPER in C zero-extends the UINT, so the mapper handle is
    // 0x00000000FFFFFFFF on x64, not a sign-extended -1.
    private static readonly System.IntPtr MapperHandle = new(0xFFFFFFFFL);

    [DllImport("winmm.dll")]
    private static extern uint waveInGetID(System.IntPtr hwi, out uint puDeviceID);

    [DllImport("winmm.dll")]
    private static extern uint waveInMessage(System.IntPtr hwi, uint uMsg, out uint dw1, out uint dw2);

    [DllImport("winmm.dll")]
    public static extern uint waveInReset(System.IntPtr hwi);

    [DllImport("winmm.dll")]
    private static extern uint waveOutMessage(System.IntPtr hwo, uint uMsg, out uint dw1, out uint dw2);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint waveOutGetDevCapsW(uint uDeviceID, ref WAVEOUTCAPSW pwoc, uint cbwoc);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WAVEOUTCAPSW
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint dwFormats;
        public ushort wChannels;
        public ushort wReserved1;
        public uint dwSupport;
    }

    // SetLastError so a failed event creation carries its win32_error; the
    // Deckle.Core declaration does not ask the marshaller to keep it.
    [DllImport("kernel32.dll", EntryPoint = "CreateEventW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern System.IntPtr CreateEvent(
        System.IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    // The opened handle names the device it runs on. A handle opened through
    // the wave mapper may answer WAVE_MAPPER itself (not verified on every
    // driver), so the mapper is then asked which device it resolved to.
    public static WaveDeviceIdentity ResolveCapture(System.IntPtr hWaveIn, int requestedDeviceId)
    {
        uint id = WAVE_MAPPER;
        if (hWaveIn != System.IntPtr.Zero && waveInGetID(hWaveIn, out uint opened) == 0)
            id = opened;
        if (id == WAVE_MAPPER && requestedDeviceId >= 0)
            id = (uint)requestedDeviceId;
        if (id == WAVE_MAPPER
            && waveInMessage(MapperHandle, DRVM_MAPPER_PREFERRED_GET, out uint preferred, out _) == 0)
            id = preferred;

        return new WaveDeviceIdentity(
            id == WAVE_MAPPER ? -1 : (int)id,
            id == WAVE_MAPPER ? WaveDeviceIdentity.UnknownName : CaptureName(id),
            requestedDeviceId < 0);
    }

    // Playback always goes through the wave mapper, so the identity is the
    // device the mapper resolves to at the moment of the call.
    public static WaveDeviceIdentity ResolvePlayback()
    {
        if (waveOutMessage(MapperHandle, DRVM_MAPPER_PREFERRED_GET, out uint id, out _) != 0)
            return new WaveDeviceIdentity(-1, WaveDeviceIdentity.UnknownName, IsDefault: true);

        var caps = new WAVEOUTCAPSW();
        string name = waveOutGetDevCapsW(id, ref caps, (uint)Marshal.SizeOf<WAVEOUTCAPSW>()) == 0
                      && !string.IsNullOrWhiteSpace(caps.szPname)
            ? caps.szPname.Trim()
            : WaveDeviceIdentity.UnknownName;
        return new WaveDeviceIdentity((int)id, name, IsDefault: true);
    }

    private static string CaptureName(uint id)
    {
        var caps = new NativeMethods.WAVEINCAPSW();
        return NativeMethods.waveInGetDevCapsW(id, ref caps, (uint)Marshal.SizeOf<NativeMethods.WAVEINCAPSW>()) == 0
               && !string.IsNullOrWhiteSpace(caps.szPname)
            ? caps.szPname.Trim()
            : WaveDeviceIdentity.UnknownName;
    }

    // Symbolic name of an MMRESULT, mmsystem.h, so a reader of the log does not
    // need the header to tell "allocated" from "no driver".
    public static string MmsysName(uint err) => err switch
    {
        0  => "MMSYSERR_NOERROR",
        1  => "MMSYSERR_ERROR",
        2  => "MMSYSERR_BADDEVICEID",
        3  => "MMSYSERR_NOTENABLED",
        4  => "MMSYSERR_ALLOCATED",
        5  => "MMSYSERR_INVALHANDLE",
        6  => "MMSYSERR_NODRIVER",
        7  => "MMSYSERR_NOMEM",
        8  => "MMSYSERR_NOTSUPPORTED",
        10 => "MMSYSERR_INVALFLAG",
        11 => "MMSYSERR_INVALPARAM",
        12 => "MMSYSERR_HANDLEBUSY",
        32 => "WAVERR_BADFORMAT",
        33 => "WAVERR_STILLPLAYING",
        34 => "WAVERR_UNPREPARED",
        35 => "WAVERR_SYNC",
        _  => "MMSYSERR_UNKNOWN",
    };
}
