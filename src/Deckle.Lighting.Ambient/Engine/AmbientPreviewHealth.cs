namespace Deckle.Lighting.Ambient;

// What the Settings preview must say when its picture cannot be trusted.
// Three small pure rules, kept apart from the control so a test can state
// them without a window:
//   - AmbientPreviewHealth: when the preview shows a problem bar.
//   - AmbientPreviewZones: which zone bands the preview draws.
//   - AmbientScanRelease: how long the engine waits for a scan to let go of
//     the display.

internal enum AmbientPreviewProblem
{
    None,

    // The scan could not start, or ended on its own: the preview has no
    // capture and keeps the display's outline.
    ScanFailed,

    // A capture runs but delivers no frame: the stage would stay grey with
    // nothing to say why.
    NoFrames,
}

internal static class AmbientPreviewHealth
{
    // A first frame normally comes in well under a second (the logs show
    // the capture starting 3840 × 2160 in under 100 ms); three seconds
    // leaves room for a slow GPU wake without leaving the person guessing.
    public static readonly TimeSpan NoFrameGrace = TimeSpan.FromSeconds(3);

    /// <param name="scanFailed">The preview asked for a scan and has none.</param>
    /// <param name="capturing">A run or a scan holds the display.</param>
    /// <param name="sinceLastFrame">Time since the frame count last moved,
    /// or since the capture was first seen.</param>
    public static AmbientPreviewProblem Classify(bool scanFailed, bool capturing, TimeSpan sinceLastFrame)
    {
        if (scanFailed) return AmbientPreviewProblem.ScanFailed;
        if (capturing && sinceLastFrame >= NoFrameGrace) return AmbientPreviewProblem.NoFrames;
        return AmbientPreviewProblem.None;
    }
}

internal static class AmbientPreviewZones
{
    // The zones some light follows. A light set to None, and a zone no light
    // follows, draw nothing: the preview shows the bands the lights are fed
    // from, not every band the engine could average. When the run knows its
    // lights (runLightIds), only those count, so a stale assignment of a
    // light that left the room does not draw a band.
    public static IReadOnlySet<LightZone> Used(
        IReadOnlyDictionary<string, LightZone> assignments,
        IReadOnlyCollection<string>? runLightIds = null)
    {
        var used = new HashSet<LightZone>();
        foreach (var (id, zone) in assignments)
        {
            if (zone == LightZone.None) continue;
            if (runLightIds is not null && !runLightIds.Contains(id)) continue;
            used.Add(zone);
        }
        return used;
    }
}

internal static class AmbientScanRelease
{
    // Runs the release off the caller and stops waiting after the limit.
    // Why: the scan's release takes the sampler's lock; a capture loop stuck
    // inside a frame (the log shows CaptureLoopStopTimedOut) keeps that lock,
    // and an unbounded wait kept the engine's scan gate closed for good, so
    // the next Ambient Light start waited on it forever and said nothing
    // (Louis, 2026-11-07). A release that overruns is abandoned, not awaited.
    public static async Task<(bool Completed, Exception? Failure)> RunBoundedAsync(
        Func<Task<Exception?>> release, TimeSpan limit)
    {
        var task = Task.Run(release);
        var first = await Task.WhenAny(task, Task.Delay(limit)).ConfigureAwait(false);
        if (!ReferenceEquals(first, task))
        {
            // Nobody awaits it any more: observe its end so a late fault
            // does not surface as an unobserved exception.
            _ = task.ContinueWith(
                static t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return (false, null);
        }

        return (true, await task.ConfigureAwait(false));
    }
}
