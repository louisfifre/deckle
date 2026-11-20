using System;

namespace Deckle.Transcription.Whisper;

// What DXGI says this process may still place in the GPU's own memory.
// BudgetBytes is the share the OS grants the process under the current
// pressure of every other process; UsageBytes is what the process holds.
internal readonly record struct VideoMemoryReading(string Adapter, long BudgetBytes, long UsageBytes)
{
    public long AvailableBytes => Math.Max(0, BudgetBytes - UsageBytes);
}

internal readonly record struct GpuMemoryShortage(string Adapter, long AvailableBytes, long RequiredBytes);

// Decides, before whisper_init_from_file_with_params, whether a GPU load can
// fit. It must be decided here: when a Vulkan allocation fails during init,
// whisper.cpp 1.9.1 calls abort() instead of returning a null context, and
// the whole process ends with 0xC0000409 (fast-fail code 7). No managed
// handler can catch that, so the only safe GPU load is one that fits.
internal static class GpuMemoryPreflight
{
    // Beyond the weights, a large-v3 context allocates its KV caches and
    // compute buffers: about 590 MB measured on 2026-11-20 (kv self 84, kv
    // cross 252, kv pad 8, compute 241). One GiB covers that with room for
    // the driver's own rounding.
    internal const long HeadroomBytes = 1L << 30;

    public static long RequiredBytes(long modelFileBytes) => modelFileBytes + HeadroomBytes;

    // Null when the load may go to the GPU. An unreadable budget keeps the
    // GPU: the check exists to avoid a known crash, not to second-guess a
    // machine it cannot read.
    public static GpuMemoryShortage? FindShortage(long modelFileBytes, VideoMemoryReading? reading)
    {
        if (reading is not { } r || modelFileBytes <= 0) return null;

        long required = RequiredBytes(modelFileBytes);
        return r.AvailableBytes >= required
            ? null
            : new GpuMemoryShortage(r.Adapter, r.AvailableBytes, required);
    }
}
