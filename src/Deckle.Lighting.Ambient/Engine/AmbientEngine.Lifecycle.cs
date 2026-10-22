using System.Diagnostics;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Lighting;
using Deckle.Vision;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    /// <summary>
    /// Builds the owned deps (capture, output, sampler) from the host's
    /// AmbientSettings, borrows the bridge client, connects the output,
    /// picks the pipeline shape (group vs multi-light), and launches the
    /// push loop. Idempotent — calling on a running engine is a no-op.
    /// Throws <see cref="InvalidOperationException"/> when the bridge
    /// isn't paired, when the persisted IP is not a LAN address, or
    /// when no group is selected ; throws other exceptions for
    /// unexpected I/O failures (network down, bridge unreachable).
    /// In every failure path the engine transitions Off → Starting →
    /// Error → Off so subscribers can react to the transient blip,
    /// and the caller (App observer) catches + reverts Enabled to
    /// false so the UI stays honest.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning) return;

        // One id for the whole run: Starting, Started or its failure, then
        // Stopping and Stopped all carry it.
        string runId = WorkId.Next();
        _runId = runId;

        SetState(AmbientEngineState.Starting);
        DeckleAmbientSource.Log.PipelineStarting(runId);
        long startRequestedAt = Stopwatch.GetTimestamp();

        // Wait for the deferred cleanup spun by the previous Stop()
        // before we touch the owned deps. The cleanup task awaits the
        // push loop's exit and disposes capture / sampler / output —
        // skipping the wait here would race a new Start against the
        // old DXGI duplication still alive on the worker thread.
        if (_stopCleanupTask is not null)
        {
            await AwaitPipelineStepAsync(_stopCleanupTask, "previous_cleanup").ConfigureAwait(false);
            _stopCleanupTask = null;
        }

        // Defensive and idempotent : a no-op after the cleanup above and
        // on a cold start.
        await DisposeOwnedDepsAsync().ConfigureAwait(false);

        // _pushLoopTask was already awaited inside _stopCleanupTask ;
        // null it out together with the spent CTS so the new run
        // starts on a clean slate.
        _pushLoopTask = null;
        _cts?.Dispose();
        _cts = null;

        var ambient = _host.Ambient;

        // Validate pair state. Without an IP, a username, and a group
        // id, the engine has nothing to talk to. Throw rather than
        // return silently so the App observer's catch fires and
        // reverts Enabled to false — keeps the tray checkmark and the
        // AmbientPage toggle in sync with the actual pipeline state.
        string validationReason = "exception";
        try
        {
            if (string.IsNullOrEmpty(ambient.HueBridgeIp)
             || string.IsNullOrEmpty(ambient.HueBridgeId)
             || string.IsNullOrEmpty(ambient.HueUsername)
             || string.IsNullOrEmpty(ambient.HueLastGroupId))
            {
                validationReason = "not_paired";
                throw new InvalidOperationException(
                    "Hue bridge not paired or no group selected — open Settings > Ambient Light and complete the Hue pair + group selection first.");
            }

            if (!HueBridgeClient.IsPrivateBridgeIp(ambient.HueBridgeIp))
            {
                validationReason = "bridge_ip_not_private";
                throw new InvalidOperationException(
                    $"Hue bridge IP '{ambient.HueBridgeIp}' is not on a private LAN range (RFC1918 or 169.254/16) — the bridge is a local device and any other address is rejected to avoid SSRF.");
            }
        }
        catch (Exception ex)
        {
            DeckleAmbientSource.Log.PipelineStartFailed(runId, validationReason, ex.GetType().Name, ex.Message);
            SetState(AmbientEngineState.Error);
            SetState(AmbientEngineState.Off);
            throw;
        }

        _useMultiLightRequested = ambient.UseMultiLight;
        lock (_lifecycleLock)
        {
            _startAbortReason = null;
            _abortRequested = false;
        }
        Interlocked.Exchange(ref _stopRequested, 0);

        try
        {
            // ── Wire owned deps ───────────────────────────────────
            // The bridge client is borrowed from HuePairingService, never
            // disposed here. Verify the cached LAN endpoint before
            // allocating capture resources : a DHCP move is repaired through
            // one local discovery pass, while the healthy fast path stays
            // one cheap bridge-identity request.
            _bridgeClient = await HuePairingService.Instance
                .GetAvailableBridgeAsync(ct)
                .ConfigureAwait(false);

            // Build the preferred Hue output now, but do not connect yet.
            // For Entertainment v2, ConnectAsync sends CLIP v2
            // `action=start` and hands the area to the bridge. Doing that
            // before capture is ready leaves a visible gap where Hue can
            // show its own activation state before Deckle sends the first
            // screen-derived frame.
            _output = await HueLightOutputFactory
                .CreatePreferredAsync(_bridgeClient, ambient.HueLastGroupId, ct)
                .ConfigureAwait(false);
            _managedGroupId = ambient.HueLastGroupId;

            _capture = new ScreenCaptureService(
                AmbientCaptureRate.Snap(ambient.CaptureFrameRateHz));
            _capture.Start(ambient.SelectedMonitorDeviceName);

            _sampler = new FrameSampler(
                _capture.Device!,
                _capture.ContentSize,
                _capture.ActiveFormat,
                _capture.PeakLuminance);

            // Subscribe sampler to the capture pump. FrameArrived fires
            // on the capture service's worker thread (the DXGI
            // AcquireNextFrame loop) ; FrameSampler.Process is
            // thread-safe internally (lock + Volatile.Write on
            // _latestSample).
            _capture.FrameArrived += OnFrameArrived;
            _capture.FrameProcessingFailed += OnFrameProcessingFailed;

            // Stopped fires only when capture ends on its own (device lost,
            // a recovery abandoned, a crashed loop) and carries that reason —
            // transient interruptions (secure desktop, ACCESS_LOST, RDP
            // disconnect) are absorbed by the capture service's retry loop.
            // When we see this, the only sane move is to stop the engine
            // cleanly so the UI toggles back to off.
            _capture.Stopped += OnCaptureStopped;

            // FormatChanged fires when a mid-session duplication recreate
            // renegotiated a surface the current sampler can't read (HDR↔SDR
            // toggle flips FP16↔BGRA8 ; a mode change resizes it). We rebuild
            // the sampler in place so the pipeline keeps producing instead of
            // tone-mapping a stale format into dead output.
            _capture.FormatChanged += OnCaptureFormatChanged;
            ThrowIfStartAbortRequested();

            _output = await HueLightOutputFactory
                .ConnectPreparedPreferredAsync(_bridgeClient, _output, ambient.HueLastGroupId, ct)
                .ConfigureAwait(false);
            _requiresContinuousColorUpdates = _output.RequiresContinuousColorUpdates;
            ThrowIfStartAbortRequested();

            // Resolve pipeline shape after Connect (ListLightsAsync
            // needs IsConnected). Multi-light requires : caller said
            // yes, driver exposes the capability, and the driver
            // reports at least one addressable light.
            if (_useMultiLightRequested && _output is IMultiLightOutput multi)
            {
                _multiLights = await multi.ListLightsAsync(ct).ConfigureAwait(false);
                ThrowIfStartAbortRequested();
                _multiLightActive = _multiLights.Count > 0;

                if (_multiLightActive)
                {
                    _multiLastPushed = new Dictionary<string, (int, int, int)>(_multiLights.Count);
                }
                else
                {
                    DeckleAmbientSource.Log.MultiLightFallback("no_lights", multi.GetType().Name);
                }
            }
            else
            {
                if (_useMultiLightRequested)
                {
                    DeckleAmbientSource.Log.MultiLightFallback("driver_incompatible", _output!.GetType().Name);
                }
                _multiLightActive = false;
            }

            _pushRateHz = AmbientPushCadence.ResolveRateHz(
                _output.PreferredColorUpdateRateHz,
                _multiLightActive);
            _pushIntervalMs = 1000 / _pushRateHz;

            if (_output.UsesStateEventAttribution)
            {
                // Fetch the v2 ↔ v1 id maps for EventStream-driven external
                // change detection. Best effort : if the bridge happens to
                // reject (rare ; older firmware, weird LAN), we log and
                // continue without external-change detection — the engine
                // still pushes normally until the next StartAsync.
                try
                {
                    var maps = await _bridgeClient.FetchV2IdMapsAsync(ct).ConfigureAwait(false);
                    _v2LightMap = maps.Lights;
                    _v2GroupedLightMap = maps.GroupedLights;
                }
                catch (Exception ex)
                {
                    DeckleAmbientSource.Log.EventStreamSetupFailed(ex.GetType().Name, ex.Message);
                    _v2LightMap = null;
                    _v2GroupedLightMap = null;
                }
            }
            else
            {
                _v2LightMap = null;
                _v2GroupedLightMap = null;
            }
            ThrowIfStartAbortRequested();

            // Per-light config dump at engine start : an unmapped light
            // (LightZone.None) is skipped and a zero-brightness one is held
            // off, so without this log the user would think "ambient doesn't
            // drive that lamp" when configuration opted it out. The dump is
            // governed detail ; computing zone and brightness solely for the
            // log must disappear when it is off.
            if (_multiLightActive && _multiLights is not null)
            {
                bool lightConfigDetailEnabled = OperationalLogAdmission.IsDetailEnabled(
                    OperationalLogActivity.Ambient,
                    DeckleAmbientSource.Log,
                    System.Diagnostics.Tracing.EventLevel.Verbose,
                    (System.Diagnostics.Tracing.EventKeywords)Deckle.Diagnostics.Keywords.Lifecycle);
                if (lightConfigDetailEnabled)
                {
                    var zoneAssignments = _host.Ambient.LightZones;
                    var lightBrightness = _host.Ambient.LightBrightness;
                    foreach (var light in _multiLights)
                    {
                        LightZone zone = (zoneAssignments is not null && zoneAssignments.TryGetValue(light.Id, out var z))
                            ? z : LightZone.None;
                        double bri = 1.0;
                        if (lightBrightness is not null && lightBrightness.TryGetValue(light.Id, out var b))
                            bri = Math.Clamp(b, 0.0, 1.0);
                        bool controlled = zone != LightZone.None && bri > 0.0;
                        DeckleAmbientSource.Log.PipelinePerLightConfig(
                            light.Id, light.Name, zone.ToString(), bri, controlled);
                    }
                }
            }

            _cts = new CancellationTokenSource();
            _startTimestamp = Stopwatch.GetTimestamp();
            _pushedCount = 0;
            _droppedCount = 0;
            ResetPushFailureEpisode();
            _heartbeatWindow.Stop();
            ResetHeartbeatCounters();
            _lastR = _lastG = _lastB = -1;
            _smoother.Clear();
            _blackLatch.Clear();
            _lastTickTimestamp = 0;
            ClearHueAttributionStates();
            _stopReason = "user";
            lock (_emittedLock) _emittedColors.Clear();

            // Open the capture-active window. From here on, each producer
            // admits Verbose AMBIENT / SCREEN / HUE detail from this activity
            // scope plus the LogAmbientCaptureActivity toggle ; the start and
            // stop milestones do not consult it. Stop()'s cleanup closes it.
            AmbientCaptureGate.SetActive(true);

            _pushLoopTask = Task.Run(() => PushLoopAsync(_cts.Token), _cts.Token);

            // Subscribe to the bridge's v2 EventStream so external state
            // changes (Hue app, Home Assistant, physical Dimmer Switch)
            // stop the pipeline cleanly. Skip if the v2 maps weren't
            // fetched (FetchV2IdMapsAsync failed earlier) — without the
            // maps we can't translate events.
            if (_v2LightMap is not null || _v2GroupedLightMap is not null)
            {
                _eventStreamTask = Task.Run(
                    () => _bridgeClient.StreamEventsAsync(OnResourceUpdate, _cts.Token),
                    _cts.Token);
            }

            CommitRunningOrThrow();
            SetState(AmbientEngineState.Running);
            DeckleAmbientSource.Log.PipelineStartDetail(
                runId,
                _capture!.IsRunning ? "running" : "stopped",
                _sampler!.GridCols,
                _sampler.GridRows);
            DeckleAmbientSource.Log.PipelineStarted(
                runId,
                _output!.GetType().Name,
                _multiLightActive ? "multi" : "group",
                _multiLights?.Count ?? 0,
                _pushRateHz,
                _sampler.IsHdr ? "on" : "off");
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested)
            {
                long elapsedMs = (Stopwatch.GetTimestamp() - startRequestedAt) * 1000 / Stopwatch.Frequency;
                DeckleAmbientSource.Log.PipelineStartCancelled(runId, elapsedMs);
            }
            else
            {
                DeckleAmbientSource.Log.PipelineStartFailed(
                    runId, _startAbortReason ?? "exception", ex.GetType().Name, ex.Message);
            }

            CancelRunBestEffort();
            AmbientCaptureGate.SetActive(false);
            IsRunning = false;

            if (_pushLoopTask is not null)
            {
                await AwaitPipelineStepAsync(_pushLoopTask, "push_loop").ConfigureAwait(false);
                _pushLoopTask = null;
            }

            await DisposeOwnedDepsAsync().ConfigureAwait(false);
            AbandonOpenIncidents(runId);
            _cts?.Dispose();
            _cts = null;
            SetState(AmbientEngineState.Error);
            SetState(AmbientEngineState.Off);
            throw;
        }
    }

    // Returns the first release failure so the stop milestone can carry it;
    // every failure is also reported on its own, per component.
    private async Task<Exception?> DisposeOwnedDepsAsync()
    {
        Exception? firstFailure = null;
        if (_capture is not null)
        {
            DetachCaptureHandlers(_capture);
            try { _capture.Dispose(); }
            catch (Exception ex) { firstFailure ??= ReportReleaseFailure("capture", ex); }
            _capture = null;
        }
        if (_sampler is not null)
        {
            try { await _sampler.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { firstFailure ??= ReportReleaseFailure("sampler", ex); }
            _sampler = null;
        }
        if (_output is IAsyncDisposable adisp)
        {
            try { await adisp.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { firstFailure ??= ReportReleaseFailure("output", ex); }
        }
        else if (_output is IDisposable disp)
        {
            try { disp.Dispose(); }
            catch (Exception ex) { firstFailure ??= ReportReleaseFailure("output", ex); }
        }
        _output = null;
        // _bridgeClient is borrowed from HuePairingService — do NOT
        // dispose. The service owns the lifecycle ; if the user forgot
        // the bridge mid-run, _bridgeClient may already be disposed
        // and any in-flight push will surface a HttpRequestException
        // that the next Start picks up cleanly.
        _bridgeClient = null;
        _multiLights = null;
        _multiLastPushed = null;
        _requiresContinuousColorUpdates = false;

        // EventStream task was running on _cts.Token — already cancelled
        // by Stop(). Await briefly to let the SSE socket close cleanly
        // before we drop the dictionaries it reads against.
        if (_eventStreamTask is not null)
        {
            await AwaitPipelineStepAsync(_eventStreamTask, "event_stream").ConfigureAwait(false);
            _eventStreamTask = null;
        }
        _v2LightMap = null;
        _v2GroupedLightMap = null;
        _managedGroupId = null;
        ClearHueAttributionStates();
        return firstFailure;
    }

    // Removing a handler from a field-like event cannot throw, so no guard.
    private void DetachCaptureHandlers(ScreenCaptureService capture)
    {
        capture.FrameArrived -= OnFrameArrived;
        capture.FrameProcessingFailed -= OnFrameProcessingFailed;
        capture.Stopped -= OnCaptureStopped;
        capture.FormatChanged -= OnCaptureFormatChanged;
    }

    private static Exception ReportReleaseFailure(string component, Exception ex)
    {
        DeckleAmbientSource.Log.ResourceReleaseFailed(component, ex.GetType().Name, ex.Message);
        return ex;
    }

    // Awaits a task whose failure must not interrupt the lifecycle step in
    // progress. Cancellation is the expected end of these tasks; anything
    // else was already reported where it happened, the detail keeps a trace.
    private static async Task AwaitPipelineStepAsync(Task task, string step)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: the run's token was cancelled before or while the task ran.
        }
        catch (Exception ex)
        {
            DeckleAmbientSource.Log.PipelineStepFailedDetail(step, ex.GetType().Name, ex.Message);
        }
    }

    private void CancelRunBestEffort()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (Exception ex)
        {
            // A token callback threw or the source was already disposed; the
            // loop still observes IsCancellationRequested or has already ended.
            DeckleAmbientSource.Log.PipelineStepFailedDetail("cancel", ex.GetType().Name, ex.Message);
        }
    }

    /// <summary>
    /// Cancels the push loop and spins a background task that releases
    /// the owned deps (capture / sampler / output) once the loop has
    /// exited. Idempotent — calls on an idle engine return silently.
    /// Transitions Running → Stopping → Off, firing StateChanged on
    /// each step so subscribers can render a brief "stopping"
    /// indicator before the final Off rendering. Stop itself stays
    /// non-blocking ; <see cref="StartAsync"/> and <see cref="DisposeAsync"/>
    /// await the in-flight cleanup so the engine never races a new
    /// run against a half-released DXGI duplication.
    /// </summary>
    public void Stop()
    {
        if (!IsRunning) return;
        if (Interlocked.Exchange(ref _stopRequested, 1) == 1) return;

        // Read once: a new StartAsync may replace _runId while the cleanup below
        // is still running, and Stopped must carry the id of the run it ends.
        string runId = _runId;
        string stopReason = _stopReason;

        SetState(AmbientEngineState.Stopping);
        DeckleAmbientSource.Log.PipelineStopping(runId, stopReason);

        CancelRunBestEffort();
        IsRunning = false;

        // Disconnect the FrameArrived subscription synchronously so
        // no further frames queue against the still-mapped sampler
        // while the deferred cleanup task is being scheduled.
        if (_capture is not null)
        {
            DetachCaptureHandlers(_capture);
        }

        // Spin the dep teardown on the thread pool. Awaits the push
        // loop's exit first (cancellation already triggered above),
        // then DisposeOwnedDepsAsync which releases the DXGI duplication
        // held by the capture — freeing the output for any other
        // ScreenCaptureService that wants to call DuplicateOutput1 on the same
        // monitor right after.
        var pushTask = _pushLoopTask;
        _stopCleanupTask = Task.Run(async () =>
        {
            // The finally guarantees the Stopped milestone, the closed capture
            // window and the Off state even when a release throws; without it
            // a failed cleanup left the engine Stopping with no end in the log.
            Exception? failure = null;
            try
            {
                if (pushTask is not null)
                    await AwaitPipelineStepAsync(pushTask, "push_loop").ConfigureAwait(false);
                failure = await DisposeOwnedDepsAsync().ConfigureAwait(false);
                AbandonOpenIncidents(runId);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
            finally
            {
                double durationS = (Stopwatch.GetTimestamp() - _startTimestamp) / (double)Stopwatch.Frequency;
                AmbientCaptureGate.SetActive(false);
                DeckleAmbientSource.Log.PipelineStopped(
                    runId,
                    stopReason,
                    failure is null ? "completed" : "failed",
                    durationS,
                    _pushedCount,
                    _droppedCount,
                    failure?.GetType().Name ?? "none",
                    failure?.Message ?? "none");
                SetState(AmbientEngineState.Off);
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();

        // Wait for the deferred cleanup Stop just spun on the thread
        // pool. It already awaits the push loop's exit and the
        // owned-deps disposal — DisposeAsync callers expect the engine
        // to be fully torn down on return.
        if (_stopCleanupTask is not null)
        {
            await AwaitPipelineStepAsync(_stopCleanupTask, "previous_cleanup").ConfigureAwait(false);
            _stopCleanupTask = null;
        }

        // Defensive : DisposeOwnedDepsAsync is idempotent and a no-op
        // when Stop's cleanup already ran. Kept to cover the disposal
        // of an engine that never reached the Running state (Start
        // failed before the cleanup task got wired).
        _pushLoopTask = null;
        await DisposeOwnedDepsAsync().ConfigureAwait(false);

        _cts?.Dispose();
        _cts = null;
        _multiLastPushed = null;
        _multiLights = null;
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }
    }
}
