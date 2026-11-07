using System.Runtime.InteropServices;

namespace Deckle.Audio.Internal;

// The MME device a playback actually ran on, as the log names it. DeviceId is
// the waveOut index, -1 when even the wave mapper could not say which device it
// resolves to. The capture names its device by endpoint id instead
// (CaptureDeviceIdentity).
internal readonly record struct WaveDeviceIdentity(int DeviceId, string Name, bool IsDefault)
{
    public const string UnknownName = "unknown";

    // Used when no listener is attached: resolving the name costs driver calls
    // that only the log needs.
    public static WaveDeviceIdentity Unresolved(int requestedDeviceId)
        => new(requestedDeviceId, UnknownName, requestedDeviceId < 0);
}

// winmm and kernel32 entry points the playback (SpeakerOutput) and the capture's
// event handle need and that Deckle.Core.NativeMethods does not declare. Kept
// module-local so the device identity and the native error names stay beside
// the events that carry them.
internal static class WaveDeviceInterop
{
    public const uint WAVE_MAPPER = 0xFFFFFFFF;

    // DRVM_MAPPER (0x2000) + 21, mmddk.h: asks the wave mapper which device it
    // currently resolves to, the system default device.
    private const uint DRVM_MAPPER_PREFERRED_GET = 0x2015;

    // (HWAVEOUT)WAVE_MAPPER in C zero-extends the UINT, so the mapper handle is
    // 0x00000000FFFFFFFF on x64, not a sign-extended -1.
    private static readonly System.IntPtr MapperHandle = new(0xFFFFFFFFL);

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

    // SetLastError so a failed event creation carries its win32_error (the
    // capture turns it into an HRESULT); the Deckle.Core declaration does not
    // ask the marshaller to keep it.
    [DllImport("kernel32.dll", EntryPoint = "CreateEventW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern System.IntPtr CreateEvent(
        System.IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

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
