using System.Runtime.InteropServices;

// Native libraries load from beside the assembly (portaudio.dll) or from
// System32 (winmm, ole32), never from the current directory or the PATH,
// where another portaudio.dll could stand.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32)]

namespace Deckle.Audio.Internal;

// The P/Invoke surface of portaudio.dll, the one native library the capture
// goes through. The DLL is built by scripts/setup/build-portaudio.ps1 from a
// pinned commit and ships in Native/win-x64 beside this module; every struct
// below mirrors the headers of that commit (include/portaudio.h and
// include/pa_win_wasapi.h). A struct that drifts from the DLL by one byte
// makes Pa_OpenStream refuse the stream, so PortAudioInteropTests pins the
// sizes and offsets.
//
// Only the calls the capture uses are declared. Strings come back as UTF-8:
// the WASAPI host API converts device names with CP_UTF8.
internal static unsafe partial class PortAudioInterop
{
    public const string Library = "portaudio";

    // PaErrorCode (portaudio.h): consecutive values from -10000.
    public const int NoError                    = 0;
    public const int NotInitialized             = -10000;
    public const int UnanticipatedHostError     = -9999;
    public const int InvalidChannelCount        = -9998;
    public const int InvalidSampleRate          = -9997;
    public const int InvalidDevice              = -9996;
    public const int InvalidFlag                = -9995;
    public const int SampleFormatNotSupported   = -9994;
    public const int BadIODeviceCombination     = -9993;
    public const int InsufficientMemory         = -9992;
    public const int BufferTooBig               = -9991;
    public const int BufferTooSmall             = -9990;
    public const int NullCallback               = -9989;
    public const int BadStreamPtr               = -9988;
    public const int TimedOut                   = -9987;
    public const int InternalError              = -9986;
    public const int DeviceUnavailable          = -9985;
    public const int IncompatibleHostApiSpecificStreamInfo = -9984;
    public const int StreamIsStopped            = -9983;
    public const int StreamIsNotStopped         = -9982;
    public const int InputOverflowed            = -9981;

    public const int NoDevice = -1;

    // PaHostApiTypeId.paWASAPI.
    public const int HostApiWasapi = 13;

    // PaSampleFormat.paFloat32: the only format the capture asks for. The
    // shared-mode engine mixes in float32 already; in exclusive mode
    // PortAudio widens the device's integer samples to it, which loses
    // nothing.
    public const uint SampleFormatFloat32 = 0x00000001;

    // PaStreamFlags.paClipOff: input never clips in PortAudio, but saying so
    // spares its converter the check.
    public const uint StreamFlagClipOff = 0x00000001;

    // paFramesPerBufferUnspecified: a blocking stream reads whatever the
    // caller asks; the host API keeps its own period.
    public const uint FramesPerBufferUnspecified = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct PaDeviceInfo
    {
        public int StructVersion;
        public byte* Name;
        public int HostApi;
        public int MaxInputChannels;
        public int MaxOutputChannels;
        public double DefaultLowInputLatency;
        public double DefaultLowOutputLatency;
        public double DefaultHighInputLatency;
        public double DefaultHighOutputLatency;
        public double DefaultSampleRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PaHostApiInfo
    {
        public int StructVersion;
        public int Type;
        public byte* Name;
        public int DeviceCount;
        public int DefaultInputDevice;
        public int DefaultOutputDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PaStreamParameters
    {
        public int Device;
        public int ChannelCount;
        public uint SampleFormat;
        public double SuggestedLatency;
        public void* HostApiSpecificStreamInfo;
    }

    // PaHostErrorInfo: the last error the host API reported. The WASAPI host
    // API records the HRESULT of every refused call here (LogHostError in
    // pa_win_wasapi.c), so after a failed open ErrorCode says why Windows
    // refused, where the PaError alone often says only paInvalidDevice.
    [StructLayout(LayoutKind.Sequential)]
    public struct PaHostErrorInfo
    {
        public int HostApiType;
        public int ErrorCode;
        public byte* ErrorText;
    }

    // PaWasapiFlags (pa_win_wasapi.h).
    public const uint WasapiFlagExclusive = 1 << 0;

    // PaWasapiStreamOption.eStreamOptionRaw: asks Windows for the raw
    // processing mode, without the driver's effects (APOs). PortAudio applies
    // it through IAudioClient2::SetClientProperties before Initialize; a
    // device that does not offer raw then refuses Initialize, and the open
    // fails with paInvalidDevice and AUDCLNT_E_RAW_MODE_UNSUPPORTED as the
    // host error.
    public const int WasapiStreamOptionNone = 0;
    public const int WasapiStreamOptionRaw  = 1;

    // PaWasapiStreamInfo, as of the pinned commit: 72 bytes on x64. The
    // trailing passthrough block appeared after v19.7.0 (56 bytes there);
    // Size must equal the DLL's own sizeof or the open is refused with
    // paIncompatibleHostApiSpecificStreamInfo.
    [StructLayout(LayoutKind.Sequential)]
    public struct PaWasapiStreamInfo
    {
        public uint Size;
        public int HostApiType;
        public uint Version;
        public uint Flags;
        public uint ChannelMask;
        public nint HostProcessorOutput;
        public nint HostProcessorInput;
        public int ThreadPriority;
        public int StreamCategory;
        public int StreamOption;
        public int PassthroughFormatId;
        public uint PassthroughEncodedSamplesPerSec;
        public uint PassthroughEncodedChannelCount;
        public uint PassthroughAverageBytesPerSec;

        public static PaWasapiStreamInfo Create(uint flags, int streamOption) => new()
        {
            Size = (uint)sizeof(PaWasapiStreamInfo),
            HostApiType = HostApiWasapi,
            Version = 1,
            Flags = flags,
            StreamOption = streamOption,
        };
    }

    [LibraryImport(Library)]
    public static partial int Pa_Initialize();

    [LibraryImport(Library)]
    public static partial int Pa_Terminate();

    [LibraryImport(Library)]
    public static partial byte* Pa_GetErrorText(int errorCode);

    [LibraryImport(Library)]
    public static partial PaHostErrorInfo* Pa_GetLastHostErrorInfo();

    [LibraryImport(Library)]
    public static partial int Pa_HostApiTypeIdToHostApiIndex(int type);

    // Devices across all host APIs, as the front end counted them at
    // Pa_Initialize: an index at or past it is refused by Pa_OpenStream.
    [LibraryImport(Library)]
    public static partial int Pa_GetDeviceCount();

    [LibraryImport(Library)]
    public static partial PaHostApiInfo* Pa_GetHostApiInfo(int hostApi);

    [LibraryImport(Library)]
    public static partial int Pa_HostApiDeviceIndexToDeviceIndex(int hostApi, int hostApiDeviceIndex);

    [LibraryImport(Library)]
    public static partial PaDeviceInfo* Pa_GetDeviceInfo(int device);

    [LibraryImport(Library)]
    public static partial int Pa_OpenStream(
        nint* stream,
        PaStreamParameters* inputParameters,
        PaStreamParameters* outputParameters,
        double sampleRate,
        uint framesPerBuffer,
        uint streamFlags,
        nint streamCallback,
        nint userData);

    [LibraryImport(Library)]
    public static partial int Pa_StartStream(nint stream);

    [LibraryImport(Library)]
    public static partial int Pa_StopStream(nint stream);

    [LibraryImport(Library)]
    public static partial int Pa_AbortStream(nint stream);

    [LibraryImport(Library)]
    public static partial int Pa_CloseStream(nint stream);

    // Blocks until frames frames are read. The capture never lets it block:
    // it reads only what Pa_GetStreamReadAvailable reports, so a device that
    // stops delivering cannot hang the thread past a stop request.
    [LibraryImport(Library)]
    public static partial int Pa_ReadStream(nint stream, void* buffer, uint frames);

    // Frames ready to read without blocking, or a negative PaError when the
    // device is gone (AUDCLNT_E_DEVICE_INVALIDATED from GetCurrentPadding).
    // C `signed long`, 32 bits on Windows.
    [LibraryImport(Library)]
    public static partial int Pa_GetStreamReadAvailable(nint stream);

    [LibraryImport(Library)]
    public static partial PaStreamInfo* Pa_GetStreamInfo(nint stream);

    [StructLayout(LayoutKind.Sequential)]
    public struct PaStreamInfo
    {
        public int StructVersion;
        public double InputLatency;
        public double OutputLatency;
        public double SampleRate;
    }

    // Rebuilds the WASAPI device list so a microphone plugged in after
    // Pa_Initialize appears, and one unplugged disappears. Available because
    // the DLL is built with PA_WASAPI_MAX_CONST_DEVICE_COUNT; it frees the
    // previous list, so it runs only while no stream is open.
    [LibraryImport(Library)]
    public static partial int PaWasapi_UpdateDeviceList();

    // The IMMDevice behind a PortAudio device, borrowed (no AddRef): valid
    // until the next device-list update. Its GetId is the endpoint id Deckle
    // persists, stable across plugs and reboots, where the PortAudio index
    // and the friendly name are not.
    [LibraryImport(Library)]
    public static partial int PaWasapi_GetIMMDevice(int device, nint* immDevice);

    // 1 when the device is a loopback capture of an output (what the speakers
    // play), 0 for a real input, a negative PaError when the index is invalid.
    [LibraryImport(Library)]
    public static partial int PaWasapi_IsLoopback(int device);

    public static string ErrorText(int error)
        => Marshal.PtrToStringUTF8((nint)Pa_GetErrorText(error)) ?? error.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string? Utf8(byte* text) => Marshal.PtrToStringUTF8((nint)text);

    // IMMDevice::GetId through the vtable: IUnknown's three slots, then
    // Activate (3), OpenPropertyStore (4), GetId (5). The string is
    // CoTaskMem-allocated by the device and freed here.
    private const int ImmDevice_GetId = 5;

    public static string? EndpointIdOf(nint immDevice)
    {
        if (immDevice == 0) return null;
        nint* vtbl = *(nint**)immDevice;
        var getId = (delegate* unmanaged<nint, char**, int>)vtbl[ImmDevice_GetId];
        char* id = null;
        int hr = getId(immDevice, &id);
        if (hr < 0 || id == null) return null;
        try { return new string(id); }
        finally { Marshal.FreeCoTaskMem((nint)id); }
    }
}
