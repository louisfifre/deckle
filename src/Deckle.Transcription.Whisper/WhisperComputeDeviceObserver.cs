using System;
using System.Collections.Generic;
using System.Globalization;

namespace Deckle.Transcription.Whisper;

// Tells which compute device a whisper context actually runs on, from the
// lines whisper.cpp and ggml write to the log hook during
// whisper_init_from_file_with_params. whisper.cpp exposes no API for it, and
// it falls back to the CPU on its own: use_gpu = 0, no GPU device, or a GPU
// backend that fails to initialize all end in a working CPU-only context.
//
// Two line families feed it:
//   • "whisper_backend_init_gpu: using Vulkan0 backend", "failed to
//     initialize Vulkan0 backend" or "no GPU found", on every context
//     creation: the device whisper picked, the one it gave up on, or none.
//   • "ggml_vulkan: 0 = <device name> | uma: ..." when ggml's device registry
//     first initializes: the human name behind Vulkan0. The registry is
//     process-global and prints its table once, so the names are kept for the
//     loads that follow an idle unload.
//
// One instance lives as long as the process, like the log hook that feeds it.
// Every member runs on the loading thread, inside the native init call or
// right after it, under the backend's model lock.
internal sealed class WhisperComputeDeviceObserver
{
    private const string BackendInitPrefix = "whisper_backend_init_gpu:";
    private const string VulkanTablePrefix = "ggml_vulkan: ";
    private const string VulkanDevicePrefix = "Vulkan";
    private const string Cpu = "CPU";

    // Neither GPU nor CPU can be claimed: the GPU was asked for and no line
    // said what whisper did with it.
    internal const string Unknown = "unknown";

    private readonly Dictionary<int, string> _vulkanDeviceNames = new();
    private string? _gpuDevice;
    private string? _failedGpuDevice;
    private bool _noGpuFound;

    // Open while the requested GPU keeps falling back to the CPU: the Warning
    // fires once per incident, not at every load after an idle unload.
    private bool _gpuFallbackOpen;

    // Same rule for a device no line could tell: the cause (a missing hook, a
    // changed wording) does not go away between two loads.
    private bool _deviceUnknownOpen;

    // The GPU whisper gave up on during the current load, empty when none.
    public string FailedGpuDevice => _failedGpuDevice ?? "";

    public void BeginLoad()
    {
        _gpuDevice = null;
        _failedGpuDevice = null;
        _noGpuFound = false;
    }

    // Returns true for the native line that announces a GPU init failure:
    // ReportLoad turns it into one Warning per incident, so the caller keeps
    // the raw line out of the Warning/Error channel.
    public bool Observe(string msg)
    {
        if (msg.StartsWith(BackendInitPrefix, StringComparison.Ordinal))
        {
            string body = msg.Substring(BackendInitPrefix.Length).Trim();
            if (TryBetween(body, "using ", " backend", out string? used))
            {
                _gpuDevice = used;
            }
            else if (TryBetween(body, "failed to initialize ", " backend", out string? failed))
            {
                // whisper drops the device and builds the context on the CPU.
                _gpuDevice = null;
                _failedGpuDevice = failed;
                return true;
            }
            else if (body.Contains("no GPU found", StringComparison.Ordinal))
            {
                _noGpuFound = true;
            }
            return false;
        }

        if (msg.StartsWith(VulkanTablePrefix, StringComparison.Ordinal))
        {
            string body = msg.Substring(VulkanTablePrefix.Length);
            int separator = body.IndexOf(" = ", StringComparison.Ordinal);
            if (separator <= 0
                || !int.TryParse(body.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                return false;

            string name = body.Substring(separator + 3);
            int pipe = name.IndexOf(" | ", StringComparison.Ordinal);
            if (pipe >= 0) name = name.Substring(0, pipe);
            _vulkanDeviceNames[index] = name.Trim();
        }
        return false;
    }

    // The device a failed init was building on, for the load-failure report:
    // "unknown" when the GPU was asked for and no line answered.
    public string DeviceOfCurrentLoad(bool gpuRequested)
    {
        if (_gpuDevice is not null) return _gpuDevice;
        if (!gpuRequested || _failedGpuDevice is not null || _noGpuFound) return Cpu;
        return Unknown;
    }

    // Logs the device the loaded context runs on and returns it in the
    // IAsrBackend.DetectedAccelerator vocabulary ("CPU", "Vulkan", ...), or
    // "unknown" when the GPU was asked for and no native line said whether
    // whisper kept it: reporting the CPU there would state a guess as a fact.
    public string ReportLoad(bool gpuRequested, bool logHookInstalled, long loadMs)
    {
        string device;
        string deviceName;
        string accelerator;
        string fallbackReason = "";

        if (_gpuDevice is not null)
        {
            device = _gpuDevice;
            deviceName = NameOf(_gpuDevice);
            accelerator = device.TrimEnd("0123456789".ToCharArray());
            _gpuFallbackOpen = false;
            _deviceUnknownOpen = false;
            DeckleWhisperSource.Log.ModelOnGpu(deviceName, device);
        }
        else if (!gpuRequested)
        {
            device = deviceName = accelerator = Cpu;
            _gpuFallbackOpen = false;
            _deviceUnknownOpen = false;
            DeckleWhisperSource.Log.ModelOnCpu();
        }
        else if (_failedGpuDevice is not null || _noGpuFound)
        {
            device = deviceName = accelerator = Cpu;
            fallbackReason = _failedGpuDevice is null ? "no_gpu_found" : "gpu_init_failed";
            _deviceUnknownOpen = false;
            if (!_gpuFallbackOpen)
            {
                _gpuFallbackOpen = true;
                DeckleWhisperSource.Log.ModelGpuUnavailable(fallbackReason, FailedGpuDevice);
            }
        }
        else
        {
            device = deviceName = accelerator = Unknown;
            fallbackReason = logHookInstalled ? "device_line_not_recognized" : "log_hook_unavailable";
            _gpuFallbackOpen = false;
            if (!_deviceUnknownOpen)
            {
                _deviceUnknownOpen = true;
                DeckleWhisperSource.Log.ModelDeviceUnknown(fallbackReason);
            }
        }

        DeckleWhisperSource.Log.ModelLoadComplete(
            loadMs, accelerator, device, deviceName, gpuRequested, logHookInstalled, fallbackReason);

        return accelerator;
    }

    private string NameOf(string device)
    {
        if (device.StartsWith(VulkanDevicePrefix, StringComparison.Ordinal)
            && int.TryParse(device.AsSpan(VulkanDevicePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            && _vulkanDeviceNames.TryGetValue(index, out string? name))
            return name;
        return device;
    }

    private static bool TryBetween(string body, string start, string end, out string? value)
    {
        value = null;
        if (!body.StartsWith(start, StringComparison.Ordinal)
            || !body.EndsWith(end, StringComparison.Ordinal)
            || body.Length <= start.Length + end.Length)
            return false;
        value = body.Substring(start.Length, body.Length - start.Length - end.Length);
        return true;
    }
}
