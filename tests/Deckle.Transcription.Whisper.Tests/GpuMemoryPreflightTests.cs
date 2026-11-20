using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

[Trait("Category", "unit")]
public sealed class GpuMemoryPreflightTests
{
    // The size of ggml-large-v3.bin, whose GPU load crashed Deckle.
    private const long LargeV3Bytes = 3_094L * 1024 * 1024;
    private const long Gib = 1L << 30;

    // 2026-11-20: another app held the GPU's memory, the Vulkan allocation
    // failed inside whisper_init_from_file_with_params and whisper.cpp
    // aborted the process. A budget too small for the model must send the
    // load to the CPU before whisper starts.
    [Fact]
    [Trait("Category", "regression")]
    public void LoadThatCannotFitInTheFreeVideoMemoryIsSentToTheCpu()
    {
        var reading = new VideoMemoryReading("Example GPU", BudgetBytes: 4 * Gib, UsageBytes: 3 * Gib);

        GpuMemoryShortage? shortage = GpuMemoryPreflight.FindShortage(LargeV3Bytes, reading);

        Assert.NotNull(shortage);
        Assert.Equal("Example GPU", shortage.Value.Adapter);
        Assert.Equal(1 * Gib, shortage.Value.AvailableBytes);
        Assert.Equal(GpuMemoryPreflight.RequiredBytes(LargeV3Bytes), shortage.Value.RequiredBytes);
    }

    [Fact]
    public void LoadThatFitsStaysOnTheGpu()
    {
        long required = GpuMemoryPreflight.RequiredBytes(LargeV3Bytes);
        var reading = new VideoMemoryReading("Example GPU", BudgetBytes: required, UsageBytes: 0);

        Assert.Null(GpuMemoryPreflight.FindShortage(LargeV3Bytes, reading));
    }

    [Fact]
    public void RequirementCoversTheBuffersBeyondTheWeights()
    {
        Assert.True(GpuMemoryPreflight.RequiredBytes(LargeV3Bytes) > LargeV3Bytes);
    }

    // The check avoids a known crash; it never blocks a GPU it cannot read.
    [Fact]
    public void UnreadableBudgetKeepsTheGpu()
    {
        Assert.Null(GpuMemoryPreflight.FindShortage(LargeV3Bytes, reading: null));
    }

    // Usage above the budget happens under pressure: the OS shrinks the
    // budget before the process gives memory back.
    [Fact]
    public void UsageAboveTheBudgetReadsAsNoFreeMemory()
    {
        var reading = new VideoMemoryReading("Example GPU", BudgetBytes: 1 * Gib, UsageBytes: 2 * Gib);

        Assert.Equal(0, reading.AvailableBytes);
        Assert.NotNull(GpuMemoryPreflight.FindShortage(LargeV3Bytes, reading));
    }
}
