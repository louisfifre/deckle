using System;
using System.Collections.Generic;
using System.Globalization;

using Deckle.Transcription;

namespace Deckle.Transcription.Whisper;

// Tells which compute device a whisper context actually runs on, from the
// lines whisper.cpp and ggml write to the log hook during
// whisper_init_from_file_with_params. whisper.cpp exposes no API for it, and
// it falls back to the CPU on its own: use_gpu = 0, no GPU device, or a GPU
// backend that fails to initialize all end in a working CPU-only context.
//
// Two line families feed it:
//   • "whisper_backend_init_gpu: using Vulkan0 backend", or "failed to
//     initialize Vulkan0 backend", on every context creation: the device
//     whisper picked, or the one it gave up on.
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

    private readonly Dictionary<int, string> _vulkanDeviceNames = new();
    private string? _gpuDevice;
    private string? _failedGpuDevice;

    // Open while the requested GPU keeps falling back to the CPU: the Warning
    // fires once per incident, not at every load after an idle unload.
    private bool _gpuFallbackOpen;

    public void BeginLoad()
    {
        _gpuDevice = null;
        _failedGpuDevice = null;
    }

    public void Observe(string msg)
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
            }
            return;
        }

        if (msg.StartsWith(VulkanTablePrefix, StringComparison.Ordinal))
        {
            string body = msg.Substring(VulkanTablePrefix.Length);
            int separator = body.IndexOf(" = ", StringComparison.Ordinal);
            if (separator <= 0
                || !int.TryParse(body.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                return;

            string name = body.Substring(separator + 3);
            int pipe = name.IndexOf(" | ", StringComparison.Ordinal);
            if (pipe >= 0) name = name.Substring(0, pipe);
            _vulkanDeviceNames[index] = name.Trim();
        }
    }

    // Logs the device the loaded context runs on and returns it in the
    // IAsrBackend.DetectedAccelerator vocabulary ("CPU", "Vulkan", ...).
    public string ReportLoad(bool gpuRequested, long loadMs)
    {
        string device = _gpuDevice ?? Cpu;
        string deviceName = _gpuDevice is null ? Cpu : NameOf(_gpuDevice);
        string accelerator = _gpuDevice is null ? Cpu : device.TrimEnd("0123456789".ToCharArray());

        if (_gpuDevice is not null)
        {
            _gpuFallbackOpen = false;
            DeckleWhispSource.Log.ModelOnGpu(deviceName);
        }
        else if (!gpuRequested)
        {
            _gpuFallbackOpen = false;
            DeckleWhispSource.Log.ModelOnCpu();
        }
        else if (!_gpuFallbackOpen)
        {
            _gpuFallbackOpen = true;
            DeckleWhispSource.Log.ModelGpuUnavailable();
        }

        DeckleWhispSource.Log.ModelLoadComplete(loadMs, accelerator, device, deviceName);

        if (gpuRequested && _gpuDevice is null)
        {
            DeckleWhispSource.Log.ModelGpuUnavailableDetail(
                _failedGpuDevice is null ? "no_gpu_found" : "gpu_init_failed",
                _failedGpuDevice ?? "");
        }

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
