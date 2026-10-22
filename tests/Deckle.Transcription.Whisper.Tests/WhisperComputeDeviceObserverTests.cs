using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

// The native lines below follow what whisper.cpp and ggml write through the
// log hook during whisper_init_from_file_with_params, with a made-up device.
[Collection(OperationalObservabilityCollection.Name)]
[Trait("Category", "observability")]
public sealed class WhisperComputeDeviceObserverTests : IDisposable
{
    private const string DeviceName = "Example GPU (vendor driver)";
    private const string Provider = "Deckle-Whisper";

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
        DeckleWhisperSource.EvtModelOnGpu,
        DeckleWhisperSource.EvtModelOnCpu,
        DeckleWhisperSource.EvtModelGpuUnavailable,
        DeckleWhisperSource.EvtModelDeviceUnknown,
        DeckleWhisperSource.EvtModelLoadComplete,
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
        using var listener = new TestEventListener(Provider);

        string accelerator = Load(observer, gpuRequested: true, [.. s_vulkanTable, .. s_gpuSelected]);

        Assert.Equal("Vulkan", accelerator);
        Assert.Collection(
            LoadEvents(listener),
            e =>
            {
                Assert.Equal(DeckleWhisperSource.EvtModelOnGpu, e.EventId);
                Assert.Equal(EventLevel.Informational, e.Level);
                Assert.Equal(DeviceName, Payload(e, "device_name"));
                Assert.Equal("Vulkan0", Payload(e, "device"));
            },
            e =>
            {
                Assert.Equal(DeckleWhisperSource.EvtModelLoadComplete, e.EventId);
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
        using var listener = new TestEventListener(Provider);

        string accelerator = Load(observer, gpuRequested: true, s_gpuSelected);

        Assert.Equal("Vulkan", accelerator);
        EventWrittenEventArgs onGpu = Assert.Single(
            LoadEvents(listener), e => e.EventId == DeckleWhisperSource.EvtModelOnGpu);
        Assert.Equal(DeviceName, Payload(onGpu, "device_name"));
    }

    // The registry still enumerates the Vulkan devices when use_gpu is off;
    // only whisper's own lines say which backend the context got.
    [Fact]
    public void GpuOffReportsTheCpuAsAskedDespiteTheVulkanTable()
    {
        var observer = new WhisperComputeDeviceObserver();
        using var listener = new TestEventListener(Provider);

        string accelerator = Load(observer, gpuRequested: false,
            [.. s_vulkanTable, "whisper_backend_init_gpu: no GPU found"]);

        Assert.Equal("CPU", accelerator);
        Assert.Collection(
            LoadEvents(listener),
            e =>
            {
                Assert.Equal(DeckleWhisperSource.EvtModelOnCpu, e.EventId);
                Assert.Equal(EventLevel.Informational, e.Level);
            },
            e =>
            {
                Assert.Equal(DeckleWhisperSource.EvtModelLoadComplete, e.EventId);
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
        using var listener = new TestEventListener(Provider);

        string accelerator = Load(observer, gpuRequested: true, failed);
        Load(observer, gpuRequested: true, failed);
        Load(observer, gpuRequested: true, s_gpuSelected);

        Assert.Equal("CPU", accelerator);
        Assert.Collection(
            LoadEvents(listener),
            e =>
            {
                Assert.Equal(DeckleWhisperSource.EvtModelGpuUnavailable, e.EventId);
                Assert.Equal(EventLevel.Warning, e.Level);
                Assert.Equal("gpu_init_failed", Payload(e, "reason"));
                Assert.Equal("Vulkan0", Payload(e, "device"));
            },
            e =>
            {
                Assert.Equal(DeckleWhisperSource.EvtModelLoadComplete, e.EventId);
                Assert.Equal("gpu_init_failed", Payload(e, "fallback_reason"));
            },
            e => Assert.Equal(DeckleWhisperSource.EvtModelLoadComplete, e.EventId),
            e => Assert.Equal(DeckleWhisperSource.EvtModelOnGpu, e.EventId),
            e => Assert.Equal(DeckleWhisperSource.EvtModelLoadComplete, e.EventId));
    }

    [Fact]
    public void RequestedGpuWithNoDeviceReportsNoGpuFound()
    {
        var observer = new WhisperComputeDeviceObserver();
        using var listener = new TestEventListener(Provider);

        string accelerator = Load(observer, gpuRequested: true, ["whisper_backend_init_gpu: no GPU found"]);

        Assert.Equal("CPU", accelerator);
        EventWrittenEventArgs unavailable = Assert.Single(
            LoadEvents(listener), e => e.EventId == DeckleWhisperSource.EvtModelGpuUnavailable);
        Assert.Equal("no_gpu_found", Payload(unavailable, "reason"));
    }

    // Without the log hook no line reaches the observer, so nothing says
    // whether whisper kept the requested GPU: claiming the CPU would send the
    // reader of a remote log after a fallback that may not exist.
    [Fact]
    [Trait("Category", "regression")]
    public void RequestedGpuWithoutTheLogHookReportsAnUnknownDeviceNotTheCpu()
    {
        var observer = new WhisperComputeDeviceObserver();
        using var listener = new TestEventListener(Provider);

        string accelerator = Load(observer, gpuRequested: true, [], logHookInstalled: false);

        Assert.Equal("unknown", accelerator);
        EventWrittenEventArgs unknown = Assert.Single(
            LoadEvents(listener), e => e.Level != EventLevel.Verbose);
        Assert.Equal(DeckleWhisperSource.EvtModelDeviceUnknown, unknown.EventId);
        Assert.Equal("log_hook_unavailable", Payload(unknown, "reason"));
    }

    // A whisper.cpp release that rewords its device line must not read as a
    // CPU fallback, and the Warning must not repeat at every reload.
    [Fact]
    [Trait("Category", "regression")]
    public void RequestedGpuWithAnUnrecognizedDeviceLineReportsAnUnknownDeviceOnce()
    {
        var observer = new WhisperComputeDeviceObserver();
        string[] reworded = ["whisper_backend_init_gpu: selected Vulkan0 for this context"];
        using var listener = new TestEventListener(Provider);

        string first = Load(observer, gpuRequested: true, reworded);
        string second = Load(observer, gpuRequested: true, reworded);

        Assert.Equal("unknown", first);
        Assert.Equal("unknown", second);
        EventWrittenEventArgs unknown = Assert.Single(
            LoadEvents(listener), e => e.Level != EventLevel.Verbose);
        Assert.Equal(DeckleWhisperSource.EvtModelDeviceUnknown, unknown.EventId);
        Assert.Equal("device_line_not_recognized", Payload(unknown, "reason"));
    }

    // A log read from another machine must tell GPU from CPU without the
    // Transcription detail switched on.
    [Fact]
    public void DeviceMilestoneReachesTheLogWhenTranscriptionDetailIsOff()
    {
        OperationalLogAdmission.SetActive(OperationalLogActivity.Transcription, true);
        OperationalLogAdmission.Configure(static _ => false);
        var observer = new WhisperComputeDeviceObserver();
        using var listener = new TestEventListener(Provider);

        Load(observer, gpuRequested: true, [.. s_vulkanTable, .. s_gpuSelected]);

        EventWrittenEventArgs onGpu = Assert.Single(
            LoadEvents(listener), e => e.Level != EventLevel.Verbose);
        Assert.Equal(DeckleWhisperSource.EvtModelOnGpu, onGpu.EventId);
        Assert.Equal(EventLevel.Informational, onGpu.Level);
        Assert.Equal(DeviceName, Payload(onGpu, "device_name"));
    }

    private static string Load(
        WhisperComputeDeviceObserver observer,
        bool gpuRequested,
        string[] nativeLines,
        bool logHookInstalled = true)
    {
        observer.BeginLoad();
        foreach (string line in nativeLines) observer.Observe(line);
        return observer.ReportLoad(gpuRequested, logHookInstalled, loadMs: 1200);
    }

    private static IReadOnlyList<EventWrittenEventArgs> LoadEvents(TestEventListener listener)
        => listener.Events.Where(e => s_loadEvents.Contains(e.EventId)).ToList();

    private static object? Payload(EventWrittenEventArgs e, string name)
        => e.Payload![e.PayloadNames!.IndexOf(name)];
}
