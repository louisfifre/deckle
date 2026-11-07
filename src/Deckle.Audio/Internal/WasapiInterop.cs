using System.Runtime.InteropServices;

namespace Deckle.Audio.Internal;

// WASAPI interop for the microphone capture: the MMDevice enumeration, the
// endpoint property store and the shared-mode IAudioClient / IAudioCaptureClient
// pair.
//
// Style mirrors MediaFoundationInterop: NO [ComImport] managed interfaces (a
// documented CsWinRT pitfall, mixing a classic RCW cast with the WinUI
// projection throws "element not found" at runtime), so every COM call goes
// through the object's vtable via a delegate* unmanaged function pointer
// indexed by the interface's method slot; IIDs and property keys are static
// readonly values; every COM pointer returned is Release()d by the caller.
//
// Every helper returns the raw HRESULT (int) instead of throwing: a refused
// stage is a status the capture logs and maps, not an exception.
//
// Slot indices count from IUnknown::QueryInterface = 0 and are stable per COM
// contract. Each interface's method order is listed beside its slots so the
// indices can be audited against mmdeviceapi.h / audioclient.h. They were also
// exercised against a live microphone before this file was written.
internal static unsafe class WasapiInterop
{
    // ── HRESULTs ─────────────────────────────────────────────────────────────

    public const int S_OK = 0;
    public const int E_NOINTERFACE = unchecked((int)0x80004002);
    public const int E_FAIL = unchecked((int)0x80004005);
    public const int E_ACCESSDENIED = unchecked((int)0x80070005);
    public const int E_INVALIDARG = unchecked((int)0x80070057);

    // HRESULT_FROM_WIN32(ERROR_NOT_FOUND): no such endpoint, or no default one.
    public const int E_NOTFOUND = unchecked((int)0x80070490);

    // audioclient.h
    public const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);
    public const int AUDCLNT_E_UNSUPPORTED_FORMAT = unchecked((int)0x88890008);
    public const int AUDCLNT_E_DEVICE_IN_USE = unchecked((int)0x8889000A);
    public const int AUDCLNT_E_ENDPOINT_CREATE_FAILED = unchecked((int)0x8889000F);
    public const int AUDCLNT_E_SERVICE_NOT_RUNNING = unchecked((int)0x88890010);
    public const int AUDCLNT_E_RESOURCES_INVALIDATED = unchecked((int)0x88890026);
    public const int AUDCLNT_E_RAW_MODE_UNSUPPORTED = unchecked((int)0x88890027);

    // Success code of GetBuffer when no packet is waiting.
    public const int AUDCLNT_S_BUFFER_EMPTY = 0x08890001;

    // ── Constants ────────────────────────────────────────────────────────────

    // EDataFlow / ERole (mmdeviceapi.h).
    public const int eCapture = 1;
    public const int eConsole = 0;

    // DEVICE_STATE_ACTIVE.
    public const uint DEVICE_STATE_ACTIVE = 0x00000001;

    // CLSCTX_ALL, for CoCreateInstance and Activate.
    public const uint CLSCTX_ALL = 0x17;

    // AUDCLNT_SHAREMODE_SHARED.
    public const int AUDCLNT_SHAREMODE_SHARED = 0;

    // AUDCLNT_STREAMFLAGS_EVENTCALLBACK: the engine signals an event each period.
    public const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;

    // AUDCLNT_STREAMOPTIONS_RAW: the stream bypasses the engine's signal
    // processing modes (no effects, no gain control, no noise suppression).
    public const uint AUDCLNT_STREAMOPTIONS_RAW = 0x1;

    // AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY / _SILENT.
    public const uint AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY = 0x1;
    public const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

    // STGM_READ.
    private const uint STGM_READ = 0x0;

    // PROPVARIANT types read here, and its size on x64 (wtypes.h / propidl.h).
    private const ushort VT_I4 = 3;
    private const ushort VT_BOOL = 11;
    private const ushort VT_UI4 = 19;
    private const ushort VT_LPWSTR = 31;
    private const int PropVariantSize = 24;
    private const int PropVariantValueOffset = 8;

    // ── CLSIDs, IIDs and property keys ───────────────────────────────────────

    public static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    public static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    public static readonly Guid IID_IAudioClient3 = new("7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42");
    public static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    // PKEY_Device_FriendlyName: the name Windows shows, "Microphone (Device)".
    public static readonly PropertyKey PKEY_Device_FriendlyName =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    // PKEY_Devices_AudioDevice_RawProcessingSupported: the endpoint's driver
    // declares that it can deliver a raw stream.
    public static readonly PropertyKey PKEY_Devices_AudioDevice_RawProcessingSupported =
        new(new Guid("8943B373-388C-4395-B557-BC6DBAFFAFDB"), 2);

    // PKEY_AudioEndpoint_Supports_EventDriven_Mode: the endpoint can signal an
    // event each period instead of being polled.
    public static readonly PropertyKey PKEY_AudioEndpoint_Supports_EventDriven_Mode =
        new(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 7);

    // ── Vtable slots ─────────────────────────────────────────────────────────
    //
    // IUnknown: 0 QueryInterface  1 AddRef  2 Release
    //
    // IMMDeviceEnumerator : IUnknown
    //   3 EnumAudioEndpoints  4 GetDefaultAudioEndpoint  5 GetDevice
    //   6 RegisterEndpointNotificationCallback  7 UnregisterEndpointNotificationCallback
    private const int Enumerator_EnumAudioEndpoints = 3;
    private const int Enumerator_GetDefaultAudioEndpoint = 4;
    private const int Enumerator_GetDevice = 5;

    // IMMDeviceCollection : IUnknown
    //   3 GetCount  4 Item
    private const int Collection_GetCount = 3;
    private const int Collection_Item = 4;

    // IMMDevice : IUnknown
    //   3 Activate  4 OpenPropertyStore  5 GetId  6 GetState
    private const int Device_Activate = 3;
    private const int Device_OpenPropertyStore = 4;
    private const int Device_GetId = 5;

    // IPropertyStore : IUnknown
    //   3 GetCount  4 GetAt  5 GetValue  6 SetValue  7 Commit
    private const int PropertyStore_GetValue = 5;

    // IAudioClient : IUnknown
    //   3 Initialize  4 GetBufferSize  5 GetStreamLatency  6 GetCurrentPadding
    //   7 IsFormatSupported  8 GetMixFormat  9 GetDevicePeriod  10 Start
    //   11 Stop  12 Reset  13 SetEventHandle  14 GetService
    // IAudioClient2 : IAudioClient
    //   15 IsOffloadCapable  16 SetClientProperties  17 GetBufferSizeLimits
    // IAudioClient3 : IAudioClient2
    //   18 GetSharedModeEnginePeriod  19 GetCurrentSharedModeEnginePeriod
    //   20 InitializeSharedAudioStream
    private const int Client_Initialize = 3;
    private const int Client_GetMixFormat = 8;
    private const int Client_GetDevicePeriod = 9;
    private const int Client_Start = 10;
    private const int Client_Stop = 11;
    private const int Client_SetEventHandle = 13;
    private const int Client_GetService = 14;
    private const int Client_SetClientProperties = 16;
    private const int Client_GetSharedModeEnginePeriod = 18;
    private const int Client_InitializeSharedAudioStream = 20;

    // IAudioCaptureClient : IUnknown
    //   3 GetBuffer  4 ReleaseBuffer  5 GetNextPacketSize
    private const int Capture_GetBuffer = 3;
    private const int Capture_ReleaseBuffer = 4;
    private const int Capture_GetNextPacketSize = 5;

    // ── Flat P/Invokes ───────────────────────────────────────────────────────

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(Guid* rclsid, nint pUnkOuter, uint dwClsContext, Guid* riid, nint* ppv);

    [DllImport("ole32.dll", ExactSpelling = true)]
    public static extern void CoTaskMemFree(nint pv);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int PropVariantClear(byte* pvar);

    // ── IUnknown ─────────────────────────────────────────────────────────────

    public static uint Release(nint instance)
    {
        if (instance == 0) return 0;
        var fn = (delegate* unmanaged<nint, uint>)(*(nint**)instance)[2];
        return fn(instance);
    }

    // ── IMMDeviceEnumerator ──────────────────────────────────────────────────

    public static int CreateDeviceEnumerator(out nint enumerator)
    {
        Guid clsid = CLSID_MMDeviceEnumerator;
        Guid iid = IID_IMMDeviceEnumerator;
        nint ptr;
        int hr = CoCreateInstance(&clsid, 0, CLSCTX_ALL, &iid, &ptr);
        enumerator = hr >= 0 ? ptr : 0;
        return hr;
    }

    public static int EnumerateEndpoints(nint enumerator, int dataFlow, uint stateMask, out nint collection)
    {
        var fn = (delegate* unmanaged<nint, int, uint, nint*, int>)(*(nint**)enumerator)[Enumerator_EnumAudioEndpoints];
        nint ptr;
        int hr = fn(enumerator, dataFlow, stateMask, &ptr);
        collection = hr >= 0 ? ptr : 0;
        return hr;
    }

    public static int GetDefaultEndpoint(nint enumerator, int dataFlow, int role, out nint device)
    {
        var fn = (delegate* unmanaged<nint, int, int, nint*, int>)(*(nint**)enumerator)[Enumerator_GetDefaultAudioEndpoint];
        nint ptr;
        int hr = fn(enumerator, dataFlow, role, &ptr);
        device = hr >= 0 ? ptr : 0;
        return hr;
    }

    public static int GetEndpoint(nint enumerator, string endpointId, out nint device)
    {
        var fn = (delegate* unmanaged<nint, char*, nint*, int>)(*(nint**)enumerator)[Enumerator_GetDevice];
        nint ptr;
        int hr;
        fixed (char* id = endpointId) hr = fn(enumerator, id, &ptr);
        device = hr >= 0 ? ptr : 0;
        return hr;
    }

    // ── IMMDeviceCollection ──────────────────────────────────────────────────

    public static int CollectionCount(nint collection, out uint count)
    {
        var fn = (delegate* unmanaged<nint, uint*, int>)(*(nint**)collection)[Collection_GetCount];
        uint value;
        int hr = fn(collection, &value);
        count = hr >= 0 ? value : 0;
        return hr;
    }

    public static int CollectionItem(nint collection, uint index, out nint device)
    {
        var fn = (delegate* unmanaged<nint, uint, nint*, int>)(*(nint**)collection)[Collection_Item];
        nint ptr;
        int hr = fn(collection, index, &ptr);
        device = hr >= 0 ? ptr : 0;
        return hr;
    }

    // ── IMMDevice ────────────────────────────────────────────────────────────

    public static int DeviceId(nint device, out string id)
    {
        var fn = (delegate* unmanaged<nint, nint*, int>)(*(nint**)device)[Device_GetId];
        nint ptr;
        int hr = fn(device, &ptr);
        id = "";
        if (hr < 0 || ptr == 0) return hr;
        id = Marshal.PtrToStringUni(ptr) ?? "";
        CoTaskMemFree(ptr);
        return hr;
    }

    public static int DeviceActivate(nint device, Guid iid, out nint instance)
    {
        var fn = (delegate* unmanaged<nint, Guid*, uint, nint, nint*, int>)(*(nint**)device)[Device_Activate];
        nint ptr;
        int hr = fn(device, &iid, CLSCTX_ALL, 0, &ptr);
        instance = hr >= 0 ? ptr : 0;
        return hr;
    }

    public static int DeviceOpenPropertyStore(nint device, out nint store)
    {
        var fn = (delegate* unmanaged<nint, uint, nint*, int>)(*(nint**)device)[Device_OpenPropertyStore];
        nint ptr;
        int hr = fn(device, STGM_READ, &ptr);
        store = hr >= 0 ? ptr : 0;
        return hr;
    }

    // ── IPropertyStore ───────────────────────────────────────────────────────
    //
    // A value that is missing, empty or of another type reads as null: the
    // property is optional on an endpoint and the caller decides what absence
    // means.

    public static string? PropertyString(nint store, PropertyKey key)
    {
        byte* variant = stackalloc byte[PropVariantSize];
        if (!ReadProperty(store, key, variant, out ushort type)) return null;
        try
        {
            return type == VT_LPWSTR
                ? Marshal.PtrToStringUni(*(nint*)(variant + PropVariantValueOffset))
                : null;
        }
        finally { PropVariantClear(variant); }
    }

    public static bool? PropertyBool(nint store, PropertyKey key)
    {
        byte* variant = stackalloc byte[PropVariantSize];
        if (!ReadProperty(store, key, variant, out ushort type)) return null;
        try
        {
            return type switch
            {
                VT_BOOL => *(short*)(variant + PropVariantValueOffset) != 0,
                VT_UI4 or VT_I4 => *(int*)(variant + PropVariantValueOffset) != 0,
                _ => null,
            };
        }
        finally { PropVariantClear(variant); }
    }

    private static bool ReadProperty(nint store, PropertyKey key, byte* variant, out ushort type)
    {
        new Span<byte>(variant, PropVariantSize).Clear();
        var fn = (delegate* unmanaged<nint, PropertyKey*, byte*, int>)(*(nint**)store)[PropertyStore_GetValue];
        int hr = fn(store, &key, variant);
        type = *(ushort*)variant;
        return hr >= 0 && type != 0;
    }

    // ── IAudioClient (and 2, 3) ──────────────────────────────────────────────

    // SetClientProperties with the raw option. AudioClientProperties is
    // { UINT32 cbSize; BOOL bIsOffload; AUDIO_STREAM_CATEGORY eCategory;
    //   AUDCLNT_STREAMOPTIONS Options } = 16 bytes. The category stays Other (0):
    // Speech or Communications would ask Windows to apply its own processing
    // for that kind of stream, the opposite of what raw is for.
    public static int ClientSetRaw(nint client)
    {
        uint* properties = stackalloc uint[4];
        properties[0] = 16;
        properties[1] = 0;
        properties[2] = 0;
        properties[3] = AUDCLNT_STREAMOPTIONS_RAW;
        var fn = (delegate* unmanaged<nint, uint*, int>)(*(nint**)client)[Client_SetClientProperties];
        return fn(client, properties);
    }

    // The returned WAVEFORMATEX is CoTaskMem memory the caller frees.
    public static int ClientGetMixFormat(nint client, out nint format)
    {
        var fn = (delegate* unmanaged<nint, nint*, int>)(*(nint**)client)[Client_GetMixFormat];
        nint ptr;
        int hr = fn(client, &ptr);
        format = hr >= 0 ? ptr : 0;
        return hr;
    }

    // Periods in 100 ns units.
    public static int ClientGetDevicePeriod(nint client, out long defaultPeriod, out long minimumPeriod)
    {
        var fn = (delegate* unmanaged<nint, long*, long*, int>)(*(nint**)client)[Client_GetDevicePeriod];
        long d, m;
        int hr = fn(client, &d, &m);
        defaultPeriod = hr >= 0 ? d : 0;
        minimumPeriod = hr >= 0 ? m : 0;
        return hr;
    }

    public static int ClientInitialize(nint client, uint streamFlags, long bufferDuration, nint format)
    {
        var fn = (delegate* unmanaged<nint, int, uint, long, long, nint, nint, int>)(*(nint**)client)[Client_Initialize];
        return fn(client, AUDCLNT_SHAREMODE_SHARED, streamFlags, bufferDuration, 0, format, 0);
    }

    // IAudioClient3: the engine's period bounds for a format, in frames.
    public static int ClientGetSharedModeEnginePeriod(
        nint client, nint format, out uint defaultFrames, out uint fundamentalFrames,
        out uint minimumFrames, out uint maximumFrames)
    {
        var fn = (delegate* unmanaged<nint, nint, uint*, uint*, uint*, uint*, int>)(*(nint**)client)[Client_GetSharedModeEnginePeriod];
        uint d, f, mn, mx;
        int hr = fn(client, format, &d, &f, &mn, &mx);
        defaultFrames = hr >= 0 ? d : 0;
        fundamentalFrames = hr >= 0 ? f : 0;
        minimumFrames = hr >= 0 ? mn : 0;
        maximumFrames = hr >= 0 ? mx : 0;
        return hr;
    }

    public static int ClientInitializeSharedAudioStream(nint client, uint streamFlags, uint periodFrames, nint format)
    {
        var fn = (delegate* unmanaged<nint, uint, uint, nint, nint, int>)(*(nint**)client)[Client_InitializeSharedAudioStream];
        return fn(client, streamFlags, periodFrames, format, 0);
    }

    public static int ClientSetEventHandle(nint client, nint eventHandle)
    {
        var fn = (delegate* unmanaged<nint, nint, int>)(*(nint**)client)[Client_SetEventHandle];
        return fn(client, eventHandle);
    }

    public static int ClientGetCaptureClient(nint client, out nint captureClient)
    {
        Guid iid = IID_IAudioCaptureClient;
        var fn = (delegate* unmanaged<nint, Guid*, nint*, int>)(*(nint**)client)[Client_GetService];
        nint ptr;
        int hr = fn(client, &iid, &ptr);
        captureClient = hr >= 0 ? ptr : 0;
        return hr;
    }

    public static int ClientStart(nint client)
        => ((delegate* unmanaged<nint, int>)(*(nint**)client)[Client_Start])(client);

    public static int ClientStop(nint client)
        => ((delegate* unmanaged<nint, int>)(*(nint**)client)[Client_Stop])(client);

    // ── IAudioCaptureClient ──────────────────────────────────────────────────

    public static int CaptureNextPacketSize(nint capture, out uint frames)
    {
        var fn = (delegate* unmanaged<nint, uint*, int>)(*(nint**)capture)[Capture_GetNextPacketSize];
        uint value;
        int hr = fn(capture, &value);
        frames = hr >= 0 ? value : 0;
        return hr;
    }

    // The pointer is valid until ReleaseBuffer.
    public static int CaptureGetBuffer(nint capture, out byte* data, out uint frames, out uint flags)
    {
        var fn = (delegate* unmanaged<nint, byte**, uint*, uint*, ulong*, ulong*, int>)(*(nint**)capture)[Capture_GetBuffer];
        byte* d;
        uint f, fl;
        int hr = fn(capture, &d, &f, &fl, null, null);
        data = hr >= 0 ? d : null;
        frames = hr >= 0 ? f : 0;
        flags = hr >= 0 ? fl : 0;
        return hr;
    }

    public static int CaptureReleaseBuffer(nint capture, uint frames)
        => ((delegate* unmanaged<nint, uint, int>)(*(nint**)capture)[Capture_ReleaseBuffer])(capture, frames);
}
