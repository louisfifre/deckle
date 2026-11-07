using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Audio.Internal;

// Opens a microphone as a WASAPI shared-mode capture stream, asking for the
// rawest signal the endpoint can give and stepping down only when it cannot.
//
// What is asked, in order:
//   1. Raw processing (IAudioClient2::SetClientProperties with
//      AUDCLNT_STREAMOPTIONS_RAW), when the endpoint declares it supports it
//      (PKEY_Devices_AudioDevice_RawProcessingSupported) or does not say. The
//      stream then bypasses the engine's effects, gain control and noise
//      suppression: what Deckle receives is the device's mix format as the
//      driver produced it.
//   2. The default mode, when the driver does not support raw or the engine
//      refuses the raw stream.
// Inside each mode, the shortest engine period (IAudioClient3) is tried first
// and the engine's default period is the fallback, so the level reacts within
// a few milliseconds where the driver allows it.
//
// Nothing here is silent: every step down is returned as a CaptureModeFallback
// for the capture to log as what was asked and what was obtained.
//
// The format is never negotiated down to 16 kHz mono here: it is the engine's
// mix format, and Deckle's own MonoConverter and StreamingResampler do the
// rest, so the processing is the same whatever the device.
internal sealed class WasapiEndpointProvider : ICaptureEndpointProvider
{
    // 100 ns units in a millisecond.
    private const double HundredNanosecondsPerMs = 10_000.0;

    // Buffer length for an endpoint that cannot signal an event and is polled.
    private const long PolledBufferDuration = 1_000_000; // 100 ms

    public CaptureOpenResult Open(string endpointId)
    {
        nint enumerator = 0;
        nint device = 0;
        try
        {
            int hr = WasapiInterop.CreateDeviceEnumerator(out enumerator);
            if (hr < 0)
                return CaptureOpenResult.Failed("create_enumerator", hr, CaptureDeviceIdentity.Unresolved(endpointId));

            bool isDefault = string.IsNullOrEmpty(endpointId);
            hr = isDefault
                ? WasapiInterop.GetDefaultEndpoint(enumerator, WasapiInterop.eCapture, WasapiInterop.eConsole, out device)
                : WasapiInterop.GetEndpoint(enumerator, endpointId, out device);
            if (hr < 0)
                return CaptureOpenResult.Failed(
                    isDefault ? "get_default_endpoint" : "get_endpoint", hr,
                    CaptureDeviceIdentity.Unresolved(endpointId));

            WasapiInterop.DeviceId(device, out string resolvedId);
            (string name, bool? rawSupported, bool? eventDriven) = ReadEndpointProperties(device);
            var identity = new CaptureDeviceIdentity(resolvedId, name, isDefault);

            var fallbacks = new List<CaptureModeFallback>();
            CaptureOpenResult? last = null;

            // Raw first, unless the driver says it cannot. An endpoint that says
            // nothing is tried: the engine's refusal is the real answer.
            if (rawSupported == false)
            {
                fallbacks.Add(new CaptureModeFallback("raw_unsupported", 0));
            }
            else
            {
                last = OpenInMode(device, identity, CaptureMode.Raw, eventDriven != false, fallbacks);
                if (last.Endpoint is not null) return last;
                if (WasapiErrors.IsDeviceLevel(last.Hresult)) return last;
                fallbacks.Add(new CaptureModeFallback("raw_refused", last.Hresult));
            }

            return OpenInMode(device, identity, CaptureMode.Default, eventDriven != false, fallbacks);
        }
        finally
        {
            WasapiInterop.Release(device);
            WasapiInterop.Release(enumerator);
        }
    }

    private static (string Name, bool? RawSupported, bool? EventDriven) ReadEndpointProperties(nint device)
    {
        if (WasapiInterop.DeviceOpenPropertyStore(device, out nint store) < 0)
            return (CaptureDeviceIdentity.UnknownName, null, null);
        try
        {
            string? name = WasapiInterop.PropertyString(store, WasapiInterop.PKEY_Device_FriendlyName);
            return (
                string.IsNullOrWhiteSpace(name) ? CaptureDeviceIdentity.UnknownName : name.Trim(),
                WasapiInterop.PropertyBool(store, WasapiInterop.PKEY_Devices_AudioDevice_RawProcessingSupported),
                WasapiInterop.PropertyBool(store, WasapiInterop.PKEY_AudioEndpoint_Supports_EventDriven_Mode));
        }
        finally { WasapiInterop.Release(store); }
    }

    // Opens the endpoint in one mode: the shortest engine period when the engine
    // offers one, the default period otherwise (or when the short one is
    // refused, on a fresh client, since a client whose Initialize failed cannot
    // be initialised again).
    private static CaptureOpenResult OpenInMode(
        nint device, CaptureDeviceIdentity identity, CaptureMode mode, bool eventDriven,
        List<CaptureModeFallback> fallbacks)
    {
        CaptureOpenResult attempt = OpenClient(device, identity, mode, eventDriven, lowLatency: true, fallbacks);

        // OpenClient reports the low-latency stage only when the short period
        // was the reason for the failure; any other failure ends the mode.
        if (attempt.Endpoint is not null || attempt.FailedStage != LowLatencyStage)
            return attempt;

        return OpenClient(device, identity, mode, eventDriven, lowLatency: false, fallbacks);
    }

    private const string LowLatencyStage = "initialize_low_latency";

    private static CaptureOpenResult OpenClient(
        nint device, CaptureDeviceIdentity identity, CaptureMode mode, bool eventDriven, bool lowLatency,
        List<CaptureModeFallback> fallbacks)
    {
        nint client = 0;
        nint format = 0;
        nint capture = 0;
        nint eventHandle = 0;
        try
        {
            // IAudioClient3 where the system has it (Windows 10 and later); the
            // base interface otherwise. Both share the slots used below.
            int hr = WasapiInterop.DeviceActivate(device, WasapiInterop.IID_IAudioClient3, out client);
            bool hasShortPeriods = hr >= 0;
            if (hr == WasapiInterop.E_NOINTERFACE)
                hr = WasapiInterop.DeviceActivate(device, WasapiInterop.IID_IAudioClient, out client);
            if (hr < 0) return CaptureOpenResult.Failed("activate", hr, identity);

            if (mode == CaptureMode.Raw)
            {
                hr = WasapiInterop.ClientSetRaw(client);
                if (hr < 0) return CaptureOpenResult.Failed("set_client_properties", hr, identity);
            }

            hr = WasapiInterop.ClientGetMixFormat(client, out format);
            if (hr < 0) return CaptureOpenResult.Failed("get_mix_format", hr, identity);

            if (!TryReadFormat(format, out CaptureFormat captureFormat))
                return CaptureOpenResult.Failed("unsupported_format", WasapiInterop.AUDCLNT_E_UNSUPPORTED_FORMAT, identity);

            uint flags = eventDriven ? WasapiInterop.AUDCLNT_STREAMFLAGS_EVENTCALLBACK : 0;
            double periodMs;
            bool shortPeriod = false;

            WasapiInterop.ClientGetDevicePeriod(client, out long defaultPeriod, out _);
            periodMs = defaultPeriod / HundredNanosecondsPerMs;

            if (lowLatency)
            {
                // Only worth a try when the engine offers a shorter period than
                // its default; if it does not, this client is initialised the
                // ordinary way and no step down is recorded.
                if (!hasShortPeriods
                    || !eventDriven
                    || WasapiInterop.ClientGetSharedModeEnginePeriod(
                        client, format, out uint defaultFrames, out _, out uint minFrames, out _) < 0
                    || minFrames >= defaultFrames)
                {
                    hr = Initialize(client, flags, eventDriven, format);
                }
                else
                {
                    hr = WasapiInterop.ClientInitializeSharedAudioStream(client, flags, minFrames, format);
                    if (hr < 0)
                    {
                        fallbacks.Add(new CaptureModeFallback("low_latency_refused", hr));
                        return CaptureOpenResult.Failed(LowLatencyStage, hr, identity);
                    }
                    shortPeriod = true;
                    periodMs = minFrames * 1000.0 / captureFormat.SampleRate;
                }
            }
            else
            {
                hr = Initialize(client, flags, eventDriven, format);
            }
            if (hr < 0) return CaptureOpenResult.Failed("initialize", hr, identity);

            if (eventDriven)
            {
                eventHandle = WaveDeviceInterop.CreateEvent(0, bManualReset: false, bInitialState: false, null);
                if (eventHandle == 0)
                    return CaptureOpenResult.Failed("create_event", Marshal.GetHRForLastWin32Error(), identity);
                hr = WasapiInterop.ClientSetEventHandle(client, eventHandle);
                if (hr < 0) return CaptureOpenResult.Failed("set_event_handle", hr, identity);
            }

            hr = WasapiInterop.ClientGetCaptureClient(client, out capture);
            if (hr < 0) return CaptureOpenResult.Failed("get_capture_client", hr, identity);

            var info = new CaptureEndpointInfo(identity, mode, captureFormat, periodMs, shortPeriod);
            var endpoint = new WasapiCaptureEndpoint(info, client, capture, eventHandle);

            // Ownership moved to the endpoint.
            client = 0;
            capture = 0;
            eventHandle = 0;
            return CaptureOpenResult.Opened(endpoint, fallbacks);
        }
        finally
        {
            if (format != 0) WasapiInterop.CoTaskMemFree(format);
            WasapiInterop.Release(capture);
            WasapiInterop.Release(client);
            if (eventHandle != 0) NativeMethods.CloseHandle(eventHandle);
        }
    }

    private static int Initialize(nint client, uint flags, bool eventDriven, nint format)
        => WasapiInterop.ClientInitialize(client, flags, eventDriven ? 0 : PolledBufferDuration, format);

    private static unsafe bool TryReadFormat(nint format, out CaptureFormat captureFormat)
    {
        // WAVEFORMATEX is 18 bytes with cbSize at offset 16; the extension follows.
        int size = 18 + *(ushort*)(format + 16);
        return CaptureFormat.TryParse(new ReadOnlySpan<byte>((void*)format, size), out captureFormat);
    }
}
