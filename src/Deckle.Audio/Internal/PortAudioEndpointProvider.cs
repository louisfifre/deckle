namespace Deckle.Audio.Internal;

// Opens a microphone through PortAudio's WASAPI host API, as directly as the
// device allows.
//
// The format is the device's own: float32 at its mix rate and channel count,
// so neither Windows nor PortAudio converts anything (asking for 16 kHz is
// refused with AUDCLNT_E_UNSUPPORTED_FORMAT unless Windows is allowed to
// resample, which is exactly what this capture avoids).
//
// The mode is the rawest the device gives, tried in this order, each step down
// recorded as a CaptureModeFallback for the log:
//   exclusive      only when the setting asks for it
//   shared + raw
//   shared + default
// An exclusive stream goes to the device past the Windows audio engine and its
// effects, so it is raw by nature and the raw option adds nothing to it. In
// shared mode PortAudio applies raw through IAudioClient2::SetClientProperties
// before IAudioClient::Initialize; a device without raw then refuses
// Initialize and the open fails, which is the signal to step down. (PortAudio
// ignores a refused SetClientProperties itself, but that call only fails where
// IAudioClient2 is missing, before Windows 8.1.) A refusal that no other mode
// can fix (the device is gone, access is denied, the audio service is down)
// ends the attempts at once.
//
// The buffer. In shared mode PortAudio sizes the device buffer from the
// suggested latency (pa_win_wasapi.c, _CalculateFramesPerHostBuffer), and a
// polled capture stream that is not read in time loses the oldest audio
// without a word: PortAudio never reads the capture flags that would say so.
// The device's low latency (about 3 ms) would leave a buffer of one or two
// packets, which a garbage collection pause empties into the void. The shared
// stream therefore asks for SharedBufferSeconds: packets still arrive every
// device period, the buffer only holds more of them when the loop is late. An
// exclusive stream keeps the device's high latency, which there is also its
// packet period.
internal sealed class PortAudioEndpointProvider : ICaptureEndpointProvider
{
    private readonly record struct Attempt(bool Exclusive, bool Raw);

    internal const double SharedBufferSeconds = 0.2;

    public CaptureOpenResult Open(string endpointId, CaptureOptions options)
    {
        endpointId ??= "";
        try
        {
            return PortAudioHost.Invoke(() => OpenOnHost(endpointId, options));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // portaudio.dll is missing or is not the build this code mirrors:
            // no microphone can be opened, and the take says so like any other
            // refused open.
            return CaptureOpenResult.Failed(
                "load", PortAudioInterop.NotInitialized, CaptureDeviceIdentity.Unresolved(endpointId), []);
        }
    }

    private static unsafe CaptureOpenResult OpenOnHost(string endpointId, CaptureOptions options)
    {
        var unresolved = CaptureDeviceIdentity.Unresolved(endpointId);
        int init = PortAudioHost.EnsureInitialized();
        if (init != 0) return CaptureOpenResult.Failed("initialize", init, unresolved, []);

        PortAudioInputDevice? found = null;
        foreach (PortAudioInputDevice candidate in PortAudioHost.ListInputs())
        {
            bool match = endpointId.Length == 0
                ? candidate.IsDefault
                : string.Equals(candidate.EndpointId, endpointId, StringComparison.OrdinalIgnoreCase);
            if (match) { found = candidate; break; }
        }

        // No default microphone, or the saved one is not plugged in.
        if (found is not PortAudioInputDevice device)
            return CaptureOpenResult.Failed("resolve", CaptureErrors.E_NOTFOUND, unresolved, []);

        var identity = new CaptureDeviceIdentity(device.EndpointId, device.Name, endpointId.Length == 0);

        Attempt[] attempts = options.Exclusive
            ? [new(true, false), new(false, true), new(false, false)]
            : [new(false, true), new(false, false)];

        var fallbacks = new List<CaptureModeFallback>();
        int lastCode = 0;
        for (int i = 0; i < attempts.Length; i++)
        {
            Attempt attempt = attempts[i];
            int code = OpenStream(device, attempt, out nint stream, out double latencySeconds, out double sampleRate);
            if (code == 0)
            {
                PortAudioHost.StreamOpened();
                var info = new CaptureEndpointInfo(
                    identity,
                    attempt.Raw || attempt.Exclusive ? CaptureMode.Raw : CaptureMode.Default,
                    attempt.Exclusive,
                    new CaptureFormat((int)Math.Round(sampleRate), device.Channels),
                    latencySeconds * 1000.0);
                return CaptureOpenResult.Opened(new PortAudioCaptureEndpoint(stream, info), fallbacks);
            }

            lastCode = code;
            if (CaptureErrors.EndsFallback(code, attempt.Exclusive)) break;
            if (i + 1 < attempts.Length)
            {
                string reason = attempts[i + 1].Exclusive != attempt.Exclusive ? "exclusive_refused" : "raw_refused";
                fallbacks.Add(new CaptureModeFallback(reason, code));
            }
        }

        return CaptureOpenResult.Failed("open", lastCode, identity, fallbacks);
    }

    private static unsafe int OpenStream(
        PortAudioInputDevice device, Attempt attempt, out nint stream, out double latencySeconds, out double sampleRate)
    {
        var wasapi = PortAudioInterop.PaWasapiStreamInfo.Create(
            attempt.Exclusive ? PortAudioInterop.WasapiFlagExclusive : 0,
            attempt.Raw ? PortAudioInterop.WasapiStreamOptionRaw : PortAudioInterop.WasapiStreamOptionNone);
        var parameters = new PortAudioInterop.PaStreamParameters
        {
            Device = device.Index,
            ChannelCount = device.Channels,
            SampleFormat = PortAudioInterop.SampleFormatFloat32,
            SuggestedLatency = attempt.Exclusive ? device.HighLatencySeconds : SharedBufferSeconds,
            HostApiSpecificStreamInfo = &wasapi,
        };

        nint opened = 0;
        int error = PortAudioInterop.Pa_OpenStream(
            &opened, &parameters, null, device.SampleRate,
            PortAudioInterop.FramesPerBufferUnspecified, PortAudioInterop.StreamFlagClipOff, 0, 0);

        stream = 0;
        latencySeconds = 0;
        sampleRate = device.SampleRate;
        if (error != 0) return PortAudioHost.ReasonOf(error);

        stream = opened;
        PortAudioInterop.PaStreamInfo* info = PortAudioInterop.Pa_GetStreamInfo(opened);
        if (info != null)
        {
            latencySeconds = info->InputLatency;
            if (info->SampleRate > 0) sampleRate = info->SampleRate;
        }
        return 0;
    }
}
