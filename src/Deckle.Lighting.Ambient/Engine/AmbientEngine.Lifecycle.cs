using System.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Lighting;
using Deckle.Vision;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    /// <summary>
    /// Builds the owned deps (capture, bridge client, output, sampler)
    /// from the host's AmbientSettings, connects the output, picks the
    /// pipeline shape (group vs multi-light), and launches the push
    /// loop. Idempotent — calling on a running engine is a no-op.
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

        SetState(AmbientEngineState.Starting);

        // Wait for the deferred cleanup spun by the previous Stop()
        // before we touch the owned deps. The cleanup task awaits the
        // push loop's exit and disposes capture / sampler / output —
        // skipping the wait here would race a new Start against the
        // old DXGI duplication still alive on the worker thread.
        if (_stopCleanupTask is not null)
        {
            try { await _stopCleanupTask.ConfigureAwait(false); } catch { }
            _stopCleanupTask = null;
        }

        // Defensive : the cleanup above always nulls the owned deps,
        // but the cold-start path (no prior Stop) lands here with
        // _capture / _sampler / _output already null and skips the
        // body. Idempotent.
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
        try
        {
            if (string.IsNullOrEmpty(ambient.HueBridgeIp)
             || string.IsNullOrEmpty(ambient.HueBridgeId)
             || string.IsNullOrEmpty(ambient.HueUsername)
             || string.IsNullOrEmpty(ambient.HueLastGroupId))
            {
                throw new InvalidOperationException(
                    "Hue bridge not paired or no group selected — open the Playground and complete the Hue pair + group selection first.");
            }

            if (!HueBridgeClient.IsPrivateBridgeIp(ambient.HueBridgeIp))
            {
                throw new InvalidOperationException(
                    $"Hue bridge IP '{ambient.HueBridgeIp}' is not on a private LAN range (RFC1918 or 169.254/16) — the bridge is a local device and any other address is rejected to avoid SSRF.");
            }
        }
        catch
        {
            SetState(AmbientEngineState.Error);
            SetState(AmbientEngineState.Off);
            throw;
        }

        // Snapshot the multi-light flag for this run. Live changes
        // via the AmbientPage (or anywhere else) only take effect at
        // the next Start because the loop shape and per-light state
        // dict are baked in here.
        _useMultiLightRequested = ambient.UseMultiLight;
        _startAbortReason = null;
        Interlocked.Exchange(ref _stopRequested, 0);

        try
        {
            // ── Wire owned deps ───────────────────────────────────
            // The bridge client is owned by HuePairingService — a
            // process-wide singleton that auto-restores from settings
            // on first access (and is shared with the Playground +
            // Settings AmbientPage so re-pairing from one surface
            // takes effect everywhere without an engine restart). The
            // engine borrows the reference, never disposes it.
            _bridgeClient = HuePairingService.Instance.Bridge
                ?? throw new InvalidOperationException(
                    "HuePairingService restored no bridge from settings — paired state in settings.json is inconsistent.");
            _output = await HueLightOutputFactory
                .CreateConnectedPreferredAsync(_bridgeClient, ambient.HueLastGroupId, ct)
                .ConfigureAwait(false);
            _managedGroupId = ambient.HueLastGroupId;

            _capture = new ScreenCaptureService();
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

            // Stopped fires only on fatal capture failure (DEVICE_REMOVED
            // / DEVICE_HUNG) — transient interruptions (secure desktop,
            // ACCESS_LOST, RDP disconnect) are absorbed by the capture
            // service's retry loop. When we see this, the only sane move
            // is to stop the engine cleanly so the UI toggles back to off.
            _capture.Stopped += OnCaptureStopped;

            // FormatChanged fires when a mid-session duplication recreate
            // renegotiated a surface the current sampler can't read (HDR↔SDR
            // toggle flips FP16↔BGRA8 ; a mode change resizes it). We rebuild
            // the sampler in place so the pipeline keeps producing instead of
            // tone-mapping a stale format into dead output.
            _capture.FormatChanged += OnCaptureFormatChanged;
            ThrowIfStartAbortRequested();

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
                    _pushIntervalMs = 1000 / MultiPushHz;
                    _multiLastPushed = new Dictionary<string, (int, int, int)>(_multiLights.Count);
                }
                else
                {
                    DeckleAmbientSource.Log.MultiLightFallbackNoLights();
                    _pushIntervalMs = 1000 / GroupPushHz;
                }
            }
            else
            {
                if (_useMultiLightRequested)
                {
                    DeckleAmbientSource.Log.MultiLightDriverIncompat();
                    DeckleAmbientSource.Log.MultiLightDriverIncompatDetail(_output!.GetType().Name);
                }
                _multiLightActive = false;
                _pushIntervalMs = 1000 / GroupPushHz;
            }

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
                    DeckleAmbientSource.Log.EventStreamSetupFailed();
                    DeckleAmbientSource.Log.EventStreamSetupFailedDetail(ex.GetType().Name, ex.Message);
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

            DeckleAmbientSource.Log.PipelineStarted();
            DeckleAmbientSource.Log.PipelineStartDetail(
                _capture!.IsRunning ? "running" : "stopped",
                _output!.GetType().Name,
                _multiLightActive ? "multi" : "group",
                _multiLights?.Count ?? 0,
                _multiLightActive ? MultiPushHz : GroupPushHz,
                _sampler!.GridCols,
                _sampler.GridRows,
                _sampler.IsHdr ? "on" : "off");

            // Per-light config dump — surfaces unmapped lights (LightZone.None)
            // and zero-brightness lights at engine start. Both states cause
            // the push loop to silently skip the light forever. Without
            // this log the user would think "ambient doesn't drive that
            // lamp" when it's actually been opted out by configuration.
            // Info level so it shows even with the capture gate off.
            if (_multiLightActive && _multiLights is not null)
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

            _cts = new CancellationTokenSource();
            _startTimestamp = Stopwatch.GetTimestamp();
            _hbTimestamp    = _startTimestamp;
            _pushedCount = 0;
            _droppedCount = 0;
            _hbTicks = _hbPushed = _hbDropped = _hbUnmappedLights = 0;
            _hbHttpDurationsMs.Clear();
            _lastR = _lastG = _lastB = -1;
            _smoothedR = _smoothedG = _smoothedB = -1f;
            _multiSmoothed.Clear();
            ClearHueAttributionStates();
            _stopReason = "user";
            lock (_emittedLock) _emittedColors.Clear();

            // Open the capture-active window AFTER the started
            // milestones (Info + Verbose mirror above) have flushed,
            // so they pass the LogWindow drop filter even with
            // LogAmbientCaptureActivity off. From here on, Verbose
            // AMBIENT / SCREEN / HUE inside the loop are candidates
            // for filtering: the App wires the central capture gate on the
            // dispatcher at boot and it combines this gate
            // with the user toggle to decide. The window closes at the top of
            // Stop() so stop milestones pass too.
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

            ThrowIfStartAbortRequested();
            IsRunning = true;
            SetState(AmbientEngineState.Running);
        }
        catch (Exception ex)
        {
            DeckleAmbientSource.Log.PipelineStartFailed();
            DeckleAmbientSource.Log.PipelineStartFailedDetail(ex.GetType().Name, ex.Message);
            try { _cts?.Cancel(); } catch { /* best effort */ }
            AmbientCaptureGate.SetActive(false);
            IsRunning = false;

            if (_pushLoopTask is not null)
            {
                try { await _pushLoopTask.ConfigureAwait(false); }
                catch { /* logged inside the loop */ }
                _pushLoopTask = null;
            }

            await DisposeOwnedDepsAsync().ConfigureAwait(false);
            _cts?.Dispose();
            _cts = null;
            SetState(AmbientEngineState.Error);
            SetState(AmbientEngineState.Off);
            throw;
        }
    }

    private async Task DisposeOwnedDepsAsync()
    {
        if (_capture is not null)
        {
            try { _capture.FrameArrived -= OnFrameArrived; } catch { }
            try { _capture.Stopped -= OnCaptureStopped; } catch { }
            try { _capture.FormatChanged -= OnCaptureFormatChanged; } catch { }
            try { _capture.Dispose(); } catch { }
            _capture = null;
        }
        if (_sampler is not null)
        {
            try { await _sampler.DisposeAsync().ConfigureAwait(false); } catch { }
            _sampler = null;
        }
        if (_output is IAsyncDisposable adisp)
        {
            try { await adisp.DisposeAsync().ConfigureAwait(false); } catch { }
        }
        else if (_output is IDisposable disp)
        {
            try { disp.Dispose(); } catch { }
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

        // EventStream task was running on _cts.Token — already cancelled
        // by Stop(). Await briefly to let the SSE socket close cleanly
        // before we drop the dictionaries it reads against.
        if (_eventStreamTask is not null)
        {
            try { await _eventStreamTask.ConfigureAwait(false); } catch { /* expected on cancel */ }
            _eventStreamTask = null;
        }
        _v2LightMap = null;
        _v2GroupedLightMap = null;
        _managedGroupId = null;
        ClearHueAttributionStates();
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

        SetState(AmbientEngineState.Stopping);

        // Close the capture-active window FIRST so the stopped
        // milestones (Info + Verbose mirror below) pass the LogWindow
        // drop filter even with LogAmbientCaptureActivity off. The
        // push loop may still emit a final tick before cancellation
        // propagates ; those late Verbose lines also pass since the
        // gate is already off.
        AmbientCaptureGate.SetActive(false);

        long endTimestamp = Stopwatch.GetTimestamp();
        double durationSec = (endTimestamp - _startTimestamp) / (double)Stopwatch.Frequency;

        try { _cts?.Cancel(); } catch { /* best effort */ }
        IsRunning = false;

        DeckleAmbientSource.Log.PipelineStopped();
        DeckleAmbientSource.Log.PipelineStopDetail(
            _stopReason,
            _multiLightActive ? "multi" : "group",
            durationSec,
            _pushedCount,
            _droppedCount);

        // Disconnect the FrameArrived subscription synchronously so
        // no further frames queue against the still-mapped sampler
        // while the deferred cleanup task is being scheduled.
        if (_capture is not null)
        {
            try { _capture.FrameArrived -= OnFrameArrived; } catch { }
            try { _capture.Stopped -= OnCaptureStopped; } catch { }
            try { _capture.FormatChanged -= OnCaptureFormatChanged; } catch { }
        }

        // Spin the dep teardown on the thread pool. Awaits the push
        // loop's exit first (cancellation already triggered above),
        // then DisposeOwnedDepsAsync which releases the DXGI duplication
        // held by the capture — freeing the output for any other
        // ScreenCaptureService (e.g. the Playground's standalone test
        // toggle) that wants to call DuplicateOutput1 on the same
        // monitor right after.
        var pushTask = _pushLoopTask;
        _stopCleanupTask = Task.Run(async () =>
        {
            if (pushTask is not null)
            {
                try { await pushTask.ConfigureAwait(false); }
                catch { /* logged inside the loop */ }
            }
            await DisposeOwnedDepsAsync().ConfigureAwait(false);
        });

        SetState(AmbientEngineState.Off);
    }

    public static LightColor SampleZone(SampledFrame sample, LightZone zone, int bandCells)
        => AmbientZoneSampler.SampleZone(sample, zone, bandCells);

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
            try { await _stopCleanupTask.ConfigureAwait(false); }
            catch { /* logged inside the loop / DisposeOwnedDepsAsync */ }
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
