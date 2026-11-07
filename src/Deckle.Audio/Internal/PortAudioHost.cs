using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Deckle.Audio.Internal;

// A microphone as PortAudio's WASAPI host API lists it.
//   Index          PortAudio's device index, valid until the next list update.
//   EndpointId     the MMDevice endpoint id, the identity Deckle persists.
//   Name           the friendly name Windows shows.
//   Channels / SampleRate   the device's mix format, what a shared stream
//                  delivers without conversion.
//   HighLatencySeconds      the latency PortAudio suggests for a robust stream.
internal readonly record struct PortAudioInputDevice(
    int Index,
    string EndpointId,
    string Name,
    int Channels,
    double SampleRate,
    double HighLatencySeconds,
    bool IsDefault);

// The one place PortAudio is initialised, listed and asked to open or close a
// stream. PortAudio is not thread-safe, and on Windows its WASAPI host API
// creates COM objects on the thread that initialises it and expects the same
// apartment afterwards. So every control call runs on one dedicated
// background thread, in the multithreaded apartment (MTA): an object created
// there can be used from any other MTA thread, which is what the capture
// threads are, so the reads (Pa_GetStreamReadAvailable, Pa_ReadStream) run on
// the thread of the take, directly, while opening, starting, stopping and
// closing are queued here.
//
// PortAudio is initialised once, at the first use, and never terminated: the
// process exit releases it. The device list is rebuilt before each listing
// and each open while no stream is open (PaWasapi_UpdateDeviceList frees the
// previous list, which an open stream may still point into). A microphone
// plugged in while the level meter runs therefore appears at the next take
// after the meter stops.
internal static class PortAudioHost
{
    private static readonly BlockingCollection<Action> s_queue = new();
    private static readonly object s_startGate = new();
    private static Thread? s_thread;

    // Touched only on the host thread.
    private static bool s_initialized;
    private static int s_initError;
    private static int s_openStreams;

    // Runs work on the host thread and returns its result, rethrowing what it
    // threw. A call made from the host thread itself runs inline.
    public static T Invoke<T>(Func<T> work)
    {
        EnsureThread();
        if (Thread.CurrentThread == s_thread) return work();

        T result = default!;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        using var done = new ManualResetEventSlim();
        s_queue.Add(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
            finally { done.Set(); }
        });
        done.Wait();
        failure?.Throw();
        return result;
    }

    // 0 once PortAudio is initialised, the PaError of Pa_Initialize otherwise.
    // A missing or unloadable portaudio.dll throws (DllNotFoundException,
    // BadImageFormatException), which the caller reports as a refused open.
    // Host thread only.
    public static int EnsureInitialized()
    {
        if (s_initialized) return 0;
        s_initError = PortAudioInterop.Pa_Initialize();
        s_initialized = s_initError == 0;
        return s_initError;
    }

    // The microphones Windows offers now, loopback captures of the outputs
    // left out (PortAudio lists them as inputs, but they record what the
    // speakers play, not a microphone). Host thread only.
    public static unsafe IReadOnlyList<PortAudioInputDevice> ListInputs()
    {
        if (EnsureInitialized() != 0) return [];
        if (s_openStreams == 0) RefreshDevices();

        int api = PortAudioInterop.Pa_HostApiTypeIdToHostApiIndex(PortAudioInterop.HostApiWasapi);
        if (api < 0) return [];
        PortAudioInterop.PaHostApiInfo* info = PortAudioInterop.Pa_GetHostApiInfo(api);
        if (info == null) return [];

        var devices = new List<PortAudioInputDevice>();
        for (int i = 0; i < info->DeviceCount; i++)
        {
            int index = PortAudioInterop.Pa_HostApiDeviceIndexToDeviceIndex(api, i);
            if (index < 0) continue;
            PortAudioInterop.PaDeviceInfo* device = PortAudioInterop.Pa_GetDeviceInfo(index);
            if (device == null || device->MaxInputChannels <= 0) continue;
            if (PortAudioInterop.PaWasapi_IsLoopback(index) == 1) continue;

            nint immDevice = 0;
            if (PortAudioInterop.PaWasapi_GetIMMDevice(index, &immDevice) != 0) continue;
            string? endpointId = PortAudioInterop.EndpointIdOf(immDevice);
            if (string.IsNullOrEmpty(endpointId)) continue;

            string name = PortAudioInterop.Utf8(device->Name)?.Trim() ?? "";
            devices.Add(new PortAudioInputDevice(
                index,
                endpointId,
                name.Length == 0 ? CaptureDeviceIdentity.UnknownName : name,
                device->MaxInputChannels,
                device->DefaultSampleRate,
                device->DefaultHighInputLatency,
                index == info->DefaultInputDevice));
        }
        return devices;
    }

    // Rebuilds the device list, the cheap way when it is enough. PortAudio's
    // front end counts the devices once, at Pa_Initialize, and refuses to open
    // an index at or past that count (pa_front.c, deviceCount_), while
    // PaWasapi_UpdateDeviceList only rebuilds the WASAPI list under it. The
    // DLL pads that list to 32 slots so the count usually stays put, but not
    // when it was empty at initialisation (Deckle started before any audio
    // device was up) or when the machine has more endpoints than that. So when
    // the two counts disagree, or the update failed and left the list empty,
    // PortAudio is terminated and initialised again, which recounts.
    // Host thread only, and only while no stream is open.
    private static unsafe void RefreshDevices()
    {
        int updated = PortAudioInterop.PaWasapi_UpdateDeviceList();
        int api = PortAudioInterop.Pa_HostApiTypeIdToHostApiIndex(PortAudioInterop.HostApiWasapi);
        PortAudioInterop.PaHostApiInfo* info = api >= 0 ? PortAudioInterop.Pa_GetHostApiInfo(api) : null;
        bool consistent = updated == 0
            && info != null
            && info->DeviceCount > 0
            && info->DeviceCount == PortAudioInterop.Pa_GetDeviceCount();
        if (consistent) return;

        PortAudioInterop.Pa_Terminate();
        s_initialized = false;
        EnsureInitialized();
    }

    // Bookkeeping of the open streams, so the device list is never rebuilt
    // under one. Host thread only.
    public static void StreamOpened() => s_openStreams++;
    public static void StreamClosed() => s_openStreams--;

    // The precise reason behind a PaError: the HRESULT the WASAPI host API
    // recorded when Windows refused, or the PaError itself when there is none.
    // Read right after the failed call, on the thread that made it; the host
    // error is process-wide and keeps its last value, so it is only trusted
    // for the PaErrors the WASAPI code raises together with one.
    public static unsafe int ReasonOf(int paError)
    {
        if (paError >= 0) return paError;
        // paInsufficientMemory is what pa_win_wasapi.c returns when
        // IMMDevice::Activate fails, a device gone between the list and the
        // open among others, with the HRESULT logged beside it.
        if (paError is not (PortAudioInterop.UnanticipatedHostError
                            or PortAudioInterop.InsufficientMemory
                            or PortAudioInterop.InvalidDevice
                            or PortAudioInterop.DeviceUnavailable
                            or PortAudioInterop.InvalidSampleRate
                            or PortAudioInterop.InvalidChannelCount
                            or PortAudioInterop.SampleFormatNotSupported))
            return paError;

        PortAudioInterop.PaHostErrorInfo* host = PortAudioInterop.Pa_GetLastHostErrorInfo();
        if (host == null || host->HostApiType != PortAudioInterop.HostApiWasapi || host->ErrorCode >= 0)
            return paError;
        return host->ErrorCode;
    }

    private static void EnsureThread()
    {
        if (s_thread is not null) return;
        lock (s_startGate)
        {
            if (s_thread is not null) return;
            var thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "Deckle.Audio.PortAudio",
            };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            s_thread = thread;
        }
    }

    private static void Run()
    {
        // The apartment is entered explicitly before PortAudio's own
        // CoInitialize (STA) runs: that call then answers RPC_E_CHANGED_MODE,
        // which PortAudio accepts, and the thread stays in the MTA.
        CoInitializeEx(0, COINIT_MULTITHREADED);
        foreach (Action work in s_queue.GetConsumingEnumerable())
            work();
    }

    private const uint COINIT_MULTITHREADED = 0x0;

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(nint reserved, uint coInit);
}
