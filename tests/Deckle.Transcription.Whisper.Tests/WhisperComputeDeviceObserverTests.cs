using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

// The native lines below follow what whisper.cpp and ggml write through the
// log hook during whisper_init_from_file_with_params, with a made-up device.
[Collection(OperationalObservabilityCollection.Name)]
[Trait("Category", "observability")]
public sealed class WhisperComputeDeviceObserverTests : IDisposable
{
    private const string DeviceName = "Example GPU (vendor driver)";

    private static readonly string[] s_vulkanTable =
    [
        "ggml_vulkan: Found 1 Vulkan devices:",
        $"ggml_vulkan: 0 = {DeviceName} | uma: 0 | fp16: 1 | bf16: 1 | warp size: 64",
    ];

    private static readonly string[] s_gpuSelected =
    [
        "whisper_backend_init_gpu: device 0: Vulkan0 (type: 1)",
        "whisper_backend_init_gpu: found GPU device 0: Vulkan0 (type: 1, cnt: 0)",
        "whisper_backend_init_gpu: using Vulkan0 backend",
    ];

    private static readonly int[] s_loadEvents =
    [
        DeckleWhispSource.EvtModelOnGpu,
        DeckleWhispSource.EvtModelOnCpu,
        DeckleWhispSource.EvtModelGpuUnavailable,
        DeckleWhispSource.EvtModelGpuUnavailableDetail,
        DeckleWhispSource.EvtModelLoadComplete,
    ];

    public WhisperComputeDeviceObserverTests()
        => OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Transcription);

    public void Dispose()
    {
        OperationalLogAdmission.SetActive(OperationalLogActivity.Transcription, false);
        OperationalLogAdmission.Configure(static _ => false);
    }

    [Fact]
    public void GpuLoadNamesTheDeviceFromTheVulkanTable()
    {
        var observer = new WhisperComputeDeviceObserver();
        using var listener = new TestEventListener("Deckle-Whisp");

        string accelerator = Load(observer, gpuRequested: true, [.. s_vulkanTable, .. s_gpuSelected]);

        Assert.Equal("Vulkan", accelerator);
        Assert.Collection(
            LoadEvents(listener),
            e =>
            {
                Assert.Equal(DeckleWhispSource.EvtModelOnGpu, e.EventId);
                Assert.Equal(EventLevel.Informational, e.Level);
                Assert.Equal(DeviceName, Payload(e, "device_name"));
            },
            e =>
            {
                Assert.Equal(DeckleWhispSource.EvtModelLoadComplete, e.EventId);
                Assert.Equal("Vulkan", Payload(e, "backend"));
                Assert.Equal("Vulkan0", Payload(e, "device"));
                Assert.Equal(DeviceName, Payload(e, "device_name"));
            });
    }

    // ggml prints its device table once per process, so a reload after the
    // idle unload sees only whisper's own lines.
    [Fact]
    public void ReloadStillNamesTheGpuWithoutTheVulkanTable()
    {
        var observer = new WhisperComputeDeviceObserver();
        Load(observer, gpuRequested: true, [.. s_vulkanTable, .. s_gpuSelected]);
        using var listener = new TestEventListener("Deckle-Whisp");

        string accelerator = Load(observer, gpuRequested: true, s_gpuSelected);

        Assert.Equal("Vulkan", accelerator);
        EventWrittenEventArgs onGpu = Assert.Single(
            LoadEvents(listener), e => e.EventId == DeckleWhispSource.EvtModelOnGpu);
        Assert.Equal(DeviceName, Payload(onGpu, "device_name"));
    }

    // The registry still enumerates the Vulkan devices when use_gpu is off;
    // only whisper's own lines say which backend the context got.
    [Fact]
    public void GpuOffReportsTheCpuAsAskedDespiteTheVulkanTable()
    {
        var observer = new WhisperComputeDeviceObserver();
        using var listener = new TestEventListener("Deckle-Whisp");

        string accelerator = Load(observer, gpuRequested: false,
            [.. s_vulkanTable, "whisper_backend_init_gpu: no GPU found"]);

        Assert.Equal("CPU", accelerator);
        Assert.Collection(
            LoadEvents(listener),
            e =>
            {
                Assert.Equal(DeckleWhispSource.EvtModelOnCpu, e.EventId);
                Assert.Equal(EventLevel.Informational, e.Level);
            },
            e =>
            {
                Assert.Equal(DeckleWhispSource.EvtModelLoadComplete, e.EventId);
                Assert.Equal("CPU", Payload(e, "backend"));
            });
    }

    [Fact]
    public void GpuInitFailureWarnsOncePerIncidentUntilTheGpuAnswers()
    {
        var observer = new WhisperComputeDeviceObserver();
        string[] failed =
        [
            .. s_gpuSelected,
            "whisper_backend_init_gpu: failed to initialize Vulkan0 backend",
        ];
        using var listener = new TestEventListener("Deckle-Whisp");

        string accelerator = Load(observer, gpuRequested: true, failed);
        Load(observer, gpuRequested: true, failed);
        Load(observer, gpuRequested: true, s_gpuSelected);

        Assert.Equal("CPU", accelerator);
        Assert.Collection(
            LoadEvents(listener),
            e =>
            {
                Assert.Equal(DeckleWhispSource.EvtModelGpuUnavailable, e.EventId);
                Assert.Equal(EventLevel.Warning, e.Level);
            },
            e => Assert.Equal(DeckleWhispSource.EvtModelLoadComplete, e.EventId),
            e =>
            {
                Assert.Equal(DeckleWhispSource.EvtModelGpuUnavailableDetail, e.EventId);
                Assert.Equal("gpu_init_failed", Payload(e, "reason"));
                Assert.Equal("Vulkan0", Payload(e, "device"));
            },
            e => Assert.Equal(DeckleWhispSource.EvtModelLoadComplete, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtModelGpuUnavailableDetail, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtModelOnGpu, e.EventId),
            e => Assert.Equal(DeckleWhispSource.EvtModelLoadComplete, e.EventId));
    }

    [Fact]
    public void RequestedGpuWithNoDeviceReportsNoGpuFound()
    {
        var observer = new WhisperComputeDeviceObserver();
        using var listener = new TestEventListener("Deckle-Whisp");

        Load(observer, gpuRequested: true, ["whisper_backend_init_gpu: no GPU found"]);

        EventWrittenEventArgs detail = Assert.Single(
            LoadEvents(listener), e => e.EventId == DeckleWhispSource.EvtModelGpuUnavailableDetail);
        Assert.Equal("no_gpu_found", Payload(detail, "reason"));
    }

    // A log read from another machine must tell GPU from CPU without the
    // Transcription detail switched on.
    [Fact]
    public void DeviceMilestoneReachesTheLogWhenTranscriptionDetailIsOff()
    {
        OperationalLogAdmission.SetActive(OperationalLogActivity.Transcription, true);
        OperationalLogAdmission.Configure(static _ => false);
        var observer = new WhisperComputeDeviceObserver();
        using var listener = new TestEventListener("Deckle-Whisp");

        Load(observer, gpuRequested: true, [.. s_vulkanTable, .. s_gpuSelected]);

        EventWrittenEventArgs onGpu = Assert.Single(
            LoadEvents(listener), e => e.Level != EventLevel.Verbose);
        Assert.Equal(DeckleWhispSource.EvtModelOnGpu, onGpu.EventId);
        Assert.Equal(EventLevel.Informational, onGpu.Level);
        Assert.Equal(DeviceName, Payload(onGpu, "device_name"));
    }

    private static string Load(WhisperComputeDeviceObserver observer, bool gpuRequested, string[] nativeLines)
    {
        observer.BeginLoad();
        foreach (string line in nativeLines) observer.Observe(line);
        return observer.ReportLoad(gpuRequested, loadMs: 1200);
    }

    private static IReadOnlyList<EventWrittenEventArgs> LoadEvents(TestEventListener listener)
        => listener.Events.Where(e => s_loadEvents.Contains(e.EventId)).ToList();

    private static object? Payload(EventWrittenEventArgs e, string name)
        => e.Payload![e.PayloadNames!.IndexOf(name)];
}
