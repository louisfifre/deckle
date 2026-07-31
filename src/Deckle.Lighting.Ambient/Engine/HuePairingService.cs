using Deckle.Lighting;

namespace Deckle.Lighting.Ambient;

// Process-wide owner of the active Hue bridge pairing. Wraps
// HueDiscovery + HueBridgeClient + AmbientSettings persistence so the
// Playground and the Settings AmbientPage share one source of truth :
// the Bridge they observe is the same instance, persistence is done
// in one place, and re-pairing from one surface is reflected
// immediately in the other via the BridgeChanged event.
//
// Why here and not under Deckle.Lighting. The service has to persist
// the Ambient user's selected bridge target, which lives in
// AmbientSettings. Lighting owns the reusable Hue driver pieces
// (client, output factory, clientkey store), but it cannot reference
// Lighting.Ambient without creating a cycle. If another module needs
// Hue pairing without Ambient, split this into a Lighting-level
// pairing coordinator plus an Ambient target store.
//
// Ownership and disposal. The service owns the HueBridgeClient
// instance after a successful pair or restore. Forget() and
// re-pair both replace the previous client and then dispose it.
// Dispose() is for shutdown — the service itself is a process
// singleton so it rarely fires in production code paths.
//
// Threading. Pair / Restore / Forget mutate _bridgeClient under a
// lock so concurrent UI clicks from Playground + Settings don't
// produce torn state. The Bridge property reads outside the lock
// (snapshot of an immutable reference) — readers see a consistent
// HueBridgeClient or null, never a half-constructed instance.
public sealed class HuePairingService : IDisposable
{
    internal const string ManualBridgeId = "manual";

    private static readonly Lazy<HuePairingService> _instance =
        new(() => new HuePairingService());
    public static HuePairingService Instance => _instance.Value;

    private readonly object _gate = new();
    private readonly SemaphoreSlim _availabilityGate = new(1, 1);
    private HueBridgeClient? _bridgeClient;
    private HueBridge?       _pairedBridge;
    private bool _disposed;

    // Default pairing window — matches the visible countdown the
    // Playground UI shows. Callers can override per-call if they need
    // a shorter window for testing.
    public static TimeSpan DefaultPairingTimeout => TimeSpan.FromSeconds(30);
    public static TimeSpan DefaultPollInterval   => TimeSpan.FromSeconds(2);

    // Lazy singleton init runs RestoreFromSettings as a side-effect on
    // first access so any caller (Playground, AmbientPage, AmbientEngine)
    // sees a restored Bridge without having to coordinate a boot-time
    // call. RestoreFromSettings is idempotent — calling it again later
    // (e.g. from a UI Refresh) re-builds the client from the current
    // persisted state, which is what the user expects after editing
    // settings.json by hand. The BridgeChanged event fires here but
    // typically has no subscribers yet — UI surfaces subscribe later
    // and re-read Bridge on their own when they open.
    private HuePairingService()
    {
        try
        {
            RestoreFromSettings();
        }
        catch (Exception ex)
        {
            DeckleAmbientSource.Log.BridgeAutoRestoreFailed();
            DeckleAmbientSource.Log.BridgeAutoRestoreFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    /// <summary>Active bridge client when paired, null otherwise.
    /// Use this for control-path calls (SetGroupColorAsync,
    /// SetLightColorAsync, IdentifyLightAsync …) — they are intentionally
    /// not wrapped here to avoid mirroring the entire HueBridgeClient
    /// surface for no added value.</summary>
    public HueBridgeClient? Bridge
    {
        get { lock (_gate) { return _bridgeClient; } }
    }

    /// <summary>Identification triple of the paired bridge (ip/id/port)
    /// without exposing the credentials. Null when not paired. Useful
    /// for the UI to display "Paired (192.168.1.5)".</summary>
    public HueBridge? PairedBridge
    {
        get { lock (_gate) { return _pairedBridge; } }
    }

    /// <summary>True when a bridge is currently paired and ready to
    /// receive REST calls. Mirrors <c>Bridge?.IsPaired</c>.</summary>
    public bool IsPaired
    {
        get { lock (_gate) { return _bridgeClient is { IsPaired: true }; } }
    }

    /// <summary>Raised after any state-changing operation (Pair,
    /// Restore, Forget). Subscribers should re-read <see cref="Bridge"/>
    /// inside the handler — the event carries no payload by design,
    /// the property is the source of truth.</summary>
    public event Action? BridgeChanged;

    /// <summary>
    /// Finds Hue bridges advertised on the local network. No request leaves
    /// the LAN; callers may expose the online method as a separate fallback.
    /// </summary>
    public Task<IReadOnlyList<HueBridge>> DiscoverAsync(CancellationToken ct = default)
        => HueDiscovery.DiscoverLocalAsync(ct);

    /// <summary>Explicit Philips-hosted fallback for a local result with no bridge.</summary>
    public Task<IReadOnlyList<HueBridge>> DiscoverViaCloudAsync(CancellationToken ct = default)
        => HueDiscovery.DiscoverViaCloudAsync(ct);

    /// <summary>
    /// Pairs with the given bridge, persists the credentials to
    /// AmbientSettings on success, replaces the active bridge client
    /// and fires <see cref="BridgeChanged"/>. Returns the credentials
    /// on success ; throws TimeoutException if the link button is not
    /// pressed within <paramref name="timeout"/>, or HuePairingException
    /// / HueBridgeUnreachableException on bridge-side / transport-side
    /// failures. The previous client (if any) is disposed before the
    /// new one takes over — re-pairing the same bridge is the same code
    /// path as pairing a fresh one.
    /// </summary>
    public async Task<HueCredentials> PairAsync(
        HueBridge bridge,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var effectiveTimeout = timeout ?? DefaultPairingTimeout;
        var effectivePoll    = pollInterval ?? DefaultPollInterval;
        bridge = await CanonicalizeManualBridgeAsync(bridge, ct).ConfigureAwait(false);

        // Build a fresh client outside the lock — pairing does HTTP
        // and may take up to `timeout`. Holding the lock that long
        // would block every reader of Bridge / IsPaired.
        var candidate = new HueBridgeClient(bridge);
        HueCredentials creds;
        try
        {
            creds = await candidate.PairAsync(effectiveTimeout, effectivePoll, ct)
                                    .ConfigureAwait(false);
        }
        catch
        {
            candidate.Dispose();
            throw;
        }

        // Commit persistence and ownership as one serialized mutation.
        // Endpoint recovery uses the same gate, so a stale recovery cannot
        // overwrite a pairing that completed while discovery was running.
        CommitPairing(candidate, bridge, creds);

        DeckleAmbientSource.Log.BridgePairingStored();
        DeckleAmbientSource.Log.BridgePairingStoredDetail(bridge.Id, creds.UsernameHead);

        BridgeChanged?.Invoke();
        return creds;
    }

    /// <summary>
    /// Rebuilds the active bridge client from values already persisted
    /// in <see cref="AmbientSettings"/> (ip / id / username). Called
    /// once at app start so the user doesn't have to re-press the link
    /// button on every boot. No-op if any of the three fields are
    /// missing or empty (treated as "not paired yet"). Fires
    /// <see cref="BridgeChanged"/> on success so any UI surface
    /// subscribed since process boot picks up the restored state.
    /// </summary>
    public void RestoreFromSettings()
    {
        HueBridge bridge;
        HueBridgeClient client;
        HueBridgeClient? previous;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var settings = AmbientSettingsService.Instance.Current;
            if (string.IsNullOrWhiteSpace(settings.HueBridgeIp) ||
                string.IsNullOrWhiteSpace(settings.HueBridgeId) ||
                string.IsNullOrWhiteSpace(settings.HueUsername))
            {
                DeckleAmbientSource.Log.BridgeRestoreSkipped();
                return;
            }

            // Default port 443 — every bridge currently in the field
            // (Hue Bridge v2, firmware-versioned 1948086000+) listens on
            // 443. v1 bridges that needed port 80 are out of scope ;
            // discovery never returns them in the cloud lookup any more.
            bridge = new HueBridge(settings.HueBridgeId!, settings.HueBridgeIp!, 443);
            var clientKey = HueClientKeyStore.TryGetClientKey(
                settings.HueBridgeId!, settings.HueUsername!) ?? "";
            var creds = new HueCredentials(settings.HueUsername!, clientKey);
            client = new HueBridgeClient(bridge, creds);

            previous = _bridgeClient;
            _bridgeClient = client;
            _pairedBridge = bridge;
        }
        previous?.Dispose();

        DeckleAmbientSource.Log.BridgeRestoredFromSettings();
        DeckleAmbientSource.Log.BridgeRestoredFromSettingsDetail(bridge.Id, bridge.InternalIpAddress);

        BridgeChanged?.Invoke();
    }

    /// <summary>
    /// Returns the restored bridge after verifying that its cached LAN address
    /// still belongs to the same Hue bridge. A failed probe triggers one local
    /// discovery pass; a uniquely authenticated match replaces the cached
    /// endpoint without requiring the link button again.
    /// </summary>
    public async Task<HueBridgeClient> GetAvailableBridgeAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _availabilityGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            HueBridgeClient current;
            HueBridge cachedBridge;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                current = GetRequiredBridgeUnsafe();
                cachedBridge = _pairedBridge
                    ?? throw new InvalidOperationException("Paired bridge identity is unavailable.");
            }
            var credentials = current.Credentials!;

            string actualBridgeId;
            try
            {
                actualBridgeId = await current.GetBridgeIdAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return await RecoverEndpointAsync(current, cachedBridge, credentials, ex, ct)
                    .ConfigureAwait(false);
            }

            if (!IsManualBridgeId(cachedBridge.Id))
            {
                if (string.Equals(cachedBridge.Id, actualBridgeId, StringComparison.OrdinalIgnoreCase))
                    return GetCurrentAfterProbe(current);

                return await RecoverEndpointAsync(
                    current,
                    cachedBridge,
                    credentials,
                    new InvalidDataException(
                        $"Cached Hue endpoint identifies as '{actualBridgeId}', expected '{cachedBridge.Id}'."),
                    ct).ConfigureAwait(false);
            }

            try
            {
                await current.ListGroupsAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return await RecoverEndpointAsync(current, cachedBridge, credentials, ex, ct)
                    .ConfigureAwait(false);
            }

            return ActivateRecoveredEndpoint(
                current,
                cachedBridge with { Id = actualBridgeId },
                cachedBridge,
                credentials);
        }
        finally
        {
            _availabilityGate.Release();
        }
    }

    /// <summary>
    /// Lists the groups (rooms, zones, entertainment areas) configured
    /// on the active bridge. Throws InvalidOperationException if no
    /// bridge is paired.
    /// </summary>
    public Task<IReadOnlyList<HueGroup>> ListGroupsAsync(CancellationToken ct = default)
        => GetRequiredBridge().ListGroupsAsync(ct);

    /// <summary>
    /// Lists the lights inside the given group on the active bridge.
    /// Throws InvalidOperationException if no bridge is paired.
    /// </summary>
    public Task<IReadOnlyList<HueLight>> ListLightsInGroupAsync(string groupId, CancellationToken ct = default)
        => GetRequiredBridge().ListLightsInGroupAsync(groupId, ct);

    /// <summary>
    /// Lists every entertainment area configured on the active bridge
    /// with per-light positions. Throws InvalidOperationException if
    /// no bridge is paired.
    /// </summary>
    public Task<IReadOnlyList<HueEntertainmentArea>> ListEntertainmentConfigurationsAsync(CancellationToken ct = default)
        => GetRequiredBridge().ListEntertainmentConfigurationsAsync(ct);

    /// <summary>
    /// Forgets the active pairing. Disposes the bridge client, clears
    /// the persisted credentials from AmbientSettings, fires
    /// <see cref="BridgeChanged"/>. The username on the bridge itself
    /// is NOT revoked — the bridge keeps it valid until the user
    /// explicitly removes it from the Hue mobile app. This is a local
    /// "forget" — pairing again later still works without a re-press
    /// of the link button if the user does it before the bridge times
    /// the username out.
    /// </summary>
    public void Forget()
    {
        HueBridgeClient? previous;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var settings = AmbientSettingsService.Instance.Current;
            if (!string.IsNullOrWhiteSpace(settings.HueBridgeId) &&
                !string.IsNullOrWhiteSpace(settings.HueUsername))
            {
                HueClientKeyStore.RemoveClientKey(settings.HueBridgeId, settings.HueUsername);
            }
            settings.HueBridgeIp = null;
            settings.HueBridgeId = null;
            settings.HueUsername = null;
            settings.HueLastGroupId = null;
            AmbientSettingsService.Instance.Save();

            previous = _bridgeClient;
            _bridgeClient = null;
            _pairedBridge = null;
        }
        previous?.Dispose();

        DeckleAmbientSource.Log.BridgeForgotten();

        BridgeChanged?.Invoke();
    }

    private static bool IsManualBridgeId(string bridgeId)
        => string.Equals(bridgeId, ManualBridgeId, StringComparison.OrdinalIgnoreCase);

    private static async Task<HueBridge> CanonicalizeManualBridgeAsync(
        HueBridge bridge,
        CancellationToken ct)
    {
        if (!IsManualBridgeId(bridge.Id)) return bridge;

        using var client = new HueBridgeClient(bridge);
        string bridgeId = await client.GetBridgeIdAsync(ct).ConfigureAwait(false);
        return bridge with { Id = bridgeId };
    }

    private async Task<HueBridgeClient> RecoverEndpointAsync(
        HueBridgeClient expectedClient,
        HueBridge cachedBridge,
        HueCredentials credentials,
        Exception cause,
        CancellationToken ct)
    {
        var discovered = await HueDiscovery.DiscoverLocalAsync(ct).ConfigureAwait(false);
        var resolution = await HueEndpointResolver.FindAsync(
            cachedBridge.Id,
            discovered,
            (bridge, token) => ValidateCandidateAsync(bridge, credentials, token),
            ct).ConfigureAwait(false);

        if (resolution.Bridge is null)
        {
            DeckleAmbientSource.Log.BridgeEndpointRecoveryFailed();
            DeckleAmbientSource.Log.BridgeEndpointRecoveryFailedDetail(
                cachedBridge.Id,
                cachedBridge.InternalIpAddress,
                resolution.Candidates,
                resolution.Valid,
                cause.GetType().Name);
            throw new HueBridgeUnreachableException(
                $"Hue bridge '{cachedBridge.Id}' could not be recovered after its cached endpoint failed.",
                cause);
        }

        return ActivateRecoveredEndpoint(
            expectedClient,
            resolution.Bridge,
            cachedBridge,
            credentials);
    }

    private static async Task<bool> ValidateCandidateAsync(
        HueBridge bridge,
        HueCredentials credentials,
        CancellationToken ct)
    {
        try
        {
            using var client = new HueBridgeClient(bridge, credentials);
            string actualBridgeId = await client.GetBridgeIdAsync(ct).ConfigureAwait(false);
            if (!string.Equals(actualBridgeId, bridge.Id, StringComparison.OrdinalIgnoreCase))
                return false;

            await client.ListGroupsAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private HueBridgeClient ActivateRecoveredEndpoint(
        HueBridgeClient expectedClient,
        HueBridge bridge,
        HueBridge previousBridge,
        HueCredentials credentials)
    {
        var next = new HueBridgeClient(bridge, credentials);
        bool identityMigrated = !string.Equals(
            previousBridge.Id,
            bridge.Id,
            StringComparison.OrdinalIgnoreCase);

        HueBridgeClient? current;
        bool activated = false;
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                current = _bridgeClient;
                if (!ReferenceEquals(current, expectedClient))
                {
                    // Pair, forget, restore, or another recovery won while the
                    // network work was in flight. Its state is authoritative.
                }
                else
                {
                    if (identityMigrated && !string.IsNullOrWhiteSpace(credentials.ClientKey))
                    {
                        HueClientKeyStore.StoreClientKey(
                            bridge.Id,
                            credentials.Username,
                            credentials.ClientKey);
                    }

                    var settings = AmbientSettingsService.Instance.Current;
                    settings.HueBridgeId = bridge.Id;
                    settings.HueBridgeIp = bridge.InternalIpAddress;
                    AmbientSettingsService.Instance.Save();

                    if (identityMigrated)
                    {
                        HueClientKeyStore.RemoveClientKey(
                            previousBridge.Id,
                            credentials.Username);
                    }

                    _bridgeClient = next;
                    _pairedBridge = bridge;
                    activated = true;
                }
            }
        }
        catch
        {
            next.Dispose();
            throw;
        }

        if (!activated)
        {
            next.Dispose();
            return current is { IsPaired: true }
                ? current
                : throw new InvalidOperationException(
                    "Hue pairing changed while bridge endpoint recovery was running.");
        }

        expectedClient.Dispose();
        DeckleAmbientSource.Log.BridgeEndpointRecovered();
        DeckleAmbientSource.Log.BridgeEndpointRecoveredDetail(
            bridge.Id,
            previousBridge.InternalIpAddress,
            bridge.InternalIpAddress,
            identityMigrated);
        BridgeChanged?.Invoke();
        return next;
    }

    private void CommitPairing(
        HueBridgeClient client,
        HueBridge bridge,
        HueCredentials credentials)
    {
        HueBridgeClient? previous = null;
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                var settings = AmbientSettingsService.Instance.Current;
                string? previousBridgeId = settings.HueBridgeId;
                string? previousUsername = settings.HueUsername;

                if (!string.IsNullOrWhiteSpace(credentials.ClientKey))
                {
                    HueClientKeyStore.StoreClientKey(
                        bridge.Id,
                        credentials.Username,
                        credentials.ClientKey);
                }

                settings.HueBridgeIp = bridge.InternalIpAddress;
                settings.HueBridgeId = bridge.Id;
                settings.HueUsername = credentials.Username;
                AmbientSettingsService.Instance.Save();

                if (!string.IsNullOrWhiteSpace(previousBridgeId) &&
                    !string.IsNullOrWhiteSpace(previousUsername) &&
                    (!string.Equals(previousBridgeId, bridge.Id, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(previousUsername, credentials.Username, StringComparison.Ordinal)))
                {
                    HueClientKeyStore.RemoveClientKey(previousBridgeId, previousUsername);
                }

                previous = _bridgeClient;
                _bridgeClient = client;
                _pairedBridge = bridge;
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }

        if (!ReferenceEquals(previous, client)) previous?.Dispose();
    }

    private HueBridgeClient GetRequiredBridge()
    {
        lock (_gate)
        {
            return GetRequiredBridgeUnsafe();
        }
    }

    private HueBridgeClient GetRequiredBridgeUnsafe()
    {
        var client = _bridgeClient;
        if (client is null || !client.IsPaired)
        {
            throw new InvalidOperationException(
                "No bridge is currently paired. Call PairAsync or RestoreFromSettings first.");
        }
        return client;
    }

    private HueBridgeClient GetCurrentAfterProbe(HueBridgeClient expectedClient)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ReferenceEquals(_bridgeClient, expectedClient)
                ? expectedClient
                : GetRequiredBridgeUnsafe();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        HueBridgeClient? previous;
        lock (_gate)
        {
            previous       = _bridgeClient;
            _bridgeClient  = null;
            _pairedBridge  = null;
        }
        previous?.Dispose();
    }
}
