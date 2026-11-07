// AmbientEngine — the sampler built as soon as the capture can feed it, and
// the clock that says when the first sampled frame came.
using System.Diagnostics;
using Deckle.Vision;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    // Desktop Duplication hands over the whole desktop once, on the first
    // acquire after the duplication is created, then only what changes. The
    // capture loop starts inside ScreenCaptureService.Start, so that first
    // frame can come before the caller has attached its handler or built its
    // sampler; dropped there, a still screen delivers nothing more until
    // something on it moves, and the preview and the lights wait for it
    // (2026-11-07: the first preview frame came late). Every capture this
    // engine owns, the run's and the preview scan's, attaches its handlers
    // before Start, and whichever comes first, the start path or the first
    // frame on the capture thread, builds the sampler; the other one finds it.
    //
    // Two builds can race; the loser's GPU textures are released at once. The
    // capture's device, size and format are set inside Start before the loop
    // runs, so either thread reads them complete.
    private FrameSampler? EnsureSampler(ScreenCaptureService? capture)
    {
        var existing = Volatile.Read(ref _sampler);
        if (existing is not null) return existing;

        var device = capture?.Device;
        if (capture is null || device is null) return null;

        var fresh = new FrameSampler(
            device,
            capture.ContentSize,
            capture.ActiveFormat,
            capture.PeakLuminance);

        var winner = Interlocked.CompareExchange(ref _sampler, fresh, null);
        if (winner is null) return fresh;

        _ = DisposeSupersededSamplerAsync(fresh);
        return winner;
    }

    // When the current run or scan asked for frames, and when the first one
    // was sampled (0 = none yet); Stopwatch ticks. The start milestone and the
    // scan's end carry the gap, so a late first frame shows in the log that
    // travels, with the handshake time beside it.
    private long _frameClockStart;
    private long _firstFrameTicks;

    private void StartFrameClock(long now)
    {
        Volatile.Write(ref _firstFrameTicks, 0);
        Volatile.Write(ref _frameClockStart, now);
    }

    // Capture thread, once per sampled frame: one read while the first frame
    // is known, which is every frame but one.
    private void NoteFrameSampled()
    {
        if (Volatile.Read(ref _firstFrameTicks) != 0) return;
        Interlocked.CompareExchange(ref _firstFrameTicks, Stopwatch.GetTimestamp(), 0);
    }

    // Milliseconds from the frame clock's start to the first sampled frame,
    // or -1 while none came.
    private long FirstFrameMs()
    {
        long first = Volatile.Read(ref _firstFrameTicks);
        if (first == 0) return -1;
        return ElapsedMs(Volatile.Read(ref _frameClockStart), first);
    }

    private static long ElapsedMs(long from, long to)
        => (to - from) * 1000 / Stopwatch.Frequency;
}
