using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace Deckle.Lighting;

// Windows DNS-SD adapter for Hue's _hue._tcp.local service. The native API is
// preferable to a hand-written multicast DNS codec: Windows already handles
// interface selection, record parsing, cache interaction and adapter changes.
internal static partial class HueLocalDiscovery
{
    private const string HueServiceType = "_hue._tcp.local";
    // DNS_REQUEST_PENDING from winerror.h. This DNS-specific status is not
    // ERROR_IO_PENDING (997): confusing the two releases a live native query.
    private const uint DnsRequestPending = 9506;
    private const uint ErrorSuccess = 0;
    private const uint ErrorInvalidParameter = 87;
    private const uint ErrorCancelled = 1223;
    private const ushort DnsTypePtr = 12;

    private static readonly TimeSpan BrowseWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ResolveWindow = TimeSpan.FromSeconds(2);

    private static readonly DnsServiceBrowseCallback _browseCallback = OnBrowseResult;
    private static readonly DnsServiceResolveComplete _resolveCallback = OnResolveResult;
    private static readonly ConcurrentDictionary<nint, object> _sessions = new();
    private static long _nextSessionContext;

    internal static bool IsRequestPending(uint status) => status == DnsRequestPending;

    public static async Task<IReadOnlyList<HueBridge>> DiscoverAsync(CancellationToken ct)
    {
        DeckleLightingSource.Log.LocalDiscoveryStarted();
        DeckleLightingSource.Log.LocalDiscoveryStartedDetail(HueServiceType);

        try
        {
            IReadOnlyList<string> serviceNames;
            using (var browse = new BrowseSession())
            {
                browse.Start();
                try
                {
                    await Task.Delay(BrowseWindow, ct).ConfigureAwait(false);
                }
                finally
                {
                    await browse.StopAsync().ConfigureAwait(false);
                }

                serviceNames = browse.ServiceNames;
            }

            var resolved = await Task.WhenAll(
                serviceNames.Select(name => ResolveAsync(name, ct)))
                .ConfigureAwait(false);

            var bridges = resolved
                .OfType<HueBridge>()
                .GroupBy(static bridge => bridge.Id, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .OrderBy(static bridge => bridge.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            DeckleLightingSource.Log.LocalDiscoveryCompleted();
            DeckleLightingSource.Log.LocalDiscoveryCompletedDetail(bridges.Length);
            foreach (var bridge in bridges)
            {
                DeckleLightingSource.Log.DiscoveryBridgeFound(
                    bridge.Id,
                    bridge.InternalIpAddress);
            }
            return bridges;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            DeckleLightingSource.Log.LocalDiscoveryFailed();
            DeckleLightingSource.Log.LocalDiscoveryFailedDetail(ex.GetType().Name, ex.Message);
            return [];
        }
    }

    private static async Task<HueBridge?> ResolveAsync(string serviceName, CancellationToken ct)
    {
        using var resolve = new ResolveSession(serviceName);
        resolve.Start();

        try
        {
            return await resolve.Completion
                .WaitAsync(ResolveWindow, ct)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await resolve.CancelAsync().ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await resolve.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static HueBridge? ReadBridge(nint instancePointer)
    {
        var instance = Marshal.PtrToStructure<DnsServiceInstance>(instancePointer);
        if (instance.Ip4Address == 0 || instance.PropertyCount == 0)
        {
            return null;
        }

        string? bridgeId = null;
        for (var i = 0; i < instance.PropertyCount; i++)
        {
            var keyPointer = Marshal.ReadIntPtr(instance.Keys, i * IntPtr.Size);
            var valuePointer = Marshal.ReadIntPtr(instance.Values, i * IntPtr.Size);
            var key = Marshal.PtrToStringUni(keyPointer);
            if (string.Equals(key, "bridgeid", StringComparison.OrdinalIgnoreCase))
            {
                bridgeId = Marshal.PtrToStringUni(valuePointer);
                break;
            }
        }

        var addressBytes = new byte[4];
        Marshal.Copy(instance.Ip4Address, addressBytes, 0, addressBytes.Length);
        return TryCreateBridge(bridgeId, addressBytes, instance.Port);
    }

    internal static HueBridge? TryCreateBridge(string? bridgeId, byte[] addressBytes, ushort port)
    {
        if (string.IsNullOrWhiteSpace(bridgeId) || addressBytes.Length != 4)
        {
            return null;
        }

        var address = new IPAddress(addressBytes).ToString();
        if (!HueBridgeClient.IsPrivateBridgeIp(address))
        {
            return null;
        }

        return new HueBridge(bridgeId.Trim(), address, port == 0 ? 443 : port);
    }

    internal static void OnBrowseResult(uint status, nint context, nint recordPointer)
    {
        try
        {
            if (!_sessions.TryGetValue(context, out var target) ||
                target is not BrowseSession session)
            {
                return;
            }

            if (status == ErrorSuccess && recordPointer != 0)
            {
                for (var current = recordPointer; current != 0;)
                {
                    var record = Marshal.PtrToStructure<DnsRecord>(current);
                    if (record.Type == DnsTypePtr && record.Data != 0)
                    {
                        var serviceName = Marshal.PtrToStringUni(record.Data);
                        if (!string.IsNullOrWhiteSpace(serviceName))
                        {
                            session.Add(serviceName);
                        }
                    }
                    current = record.Next;
                }
            }

            if (status != ErrorSuccess)
            {
                session.Complete(status);
            }
        }
        catch
        {
            // Exceptions must never escape a reverse P/Invoke callback. The
            // owning discovery operation will time out or observe cancellation.
        }
        finally
        {
            try
            {
                if (recordPointer != 0) DnsRecordListFree(recordPointer, DnsFreeRecordList);
            }
            catch { }
        }
    }

    internal static void OnResolveResult(uint status, nint context, nint instancePointer)
    {
        try
        {
            if (!_sessions.TryGetValue(context, out var target) ||
                target is not ResolveSession session)
            {
                return;
            }

            HueBridge? bridge = null;
            if (status == ErrorSuccess && instancePointer != 0)
            {
                bridge = ReadBridge(instancePointer);
            }
            session.Complete(bridge);
        }
        catch
        {
            if (_sessions.TryGetValue(context, out var target) &&
                target is ResolveSession session)
            {
                session.Complete(null);
            }
        }
        finally
        {
            try
            {
                if (instancePointer != 0) DnsServiceFreeInstance(instancePointer);
            }
            catch { }
        }
    }

    private static nint RegisterSession(object session)
    {
        while (true)
        {
            var context = (nint)Interlocked.Increment(ref _nextSessionContext);
            if (context != 0 && _sessions.TryAdd(context, session)) return context;
        }
    }

    private static void UnregisterSession(ref nint context)
    {
        if (context == 0) return;
        _sessions.TryRemove(context, out _);
        context = 0;
    }

    private sealed class BrowseSession : IDisposable
    {
        private readonly ConcurrentDictionary<string, byte> _serviceNames =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly TaskCompletionSource<uint> _stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private nint _context;
        private nint _queryName;
        private nint _request;
        private nint _cancel;
        private bool _started;

        public IReadOnlyList<string> ServiceNames => _serviceNames.Keys.ToArray();

        public void Start()
        {
            _context = RegisterSession(this);
            _queryName = Marshal.StringToHGlobalUni(HueServiceType);
            _request = Allocate(new DnsServiceBrowseRequest
            {
                Version = 1,
                InterfaceIndex = 0,
                QueryName = _queryName,
                BrowseCallback = Marshal.GetFunctionPointerForDelegate(_browseCallback),
                QueryContext = _context,
            });
            _cancel = Allocate(default(DnsServiceCancel));

            _started = true;
            uint status;
            try
            {
                status = DnsServiceBrowse(_request, _cancel);
            }
            catch
            {
                _started = false;
                throw;
            }

            if (IsRequestPending(status)) return;

            _started = false;
            throw new Win32Exception(
                (int)status,
                $"DNS-SD browse could not start (status={status}).");
        }

        public void Add(string serviceName) => _serviceNames.TryAdd(serviceName, 0);

        public void Complete(uint status) => _stopped.TrySetResult(status);

        public async Task StopAsync()
        {
            if (!_started) return;

            var status = DnsServiceBrowseCancel(_cancel);
            if (status is not (ErrorSuccess or ErrorCancelled))
            {
                throw new Win32Exception((int)status, "DNS-SD browse could not be cancelled.");
            }

            await _stopped.Task.ConfigureAwait(false);
            _started = false;
        }

        public void Dispose()
        {
            if (_started) return;
            Free(ref _request);
            Free(ref _cancel);
            Free(ref _queryName);
            UnregisterSession(ref _context);
        }
    }

    private sealed class ResolveSession : IDisposable
    {
        private readonly TaskCompletionSource<HueBridge?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private nint _context;
        private nint _queryName;
        private nint _request;
        private nint _cancel;
        private bool _pending;

        public ResolveSession(string serviceName)
        {
            _context = RegisterSession(this);
            try
            {
                _queryName = Marshal.StringToHGlobalUni(serviceName);
                _request = Allocate(new DnsServiceResolveRequest
                {
                    Version = 1,
                    InterfaceIndex = 0,
                    QueryName = _queryName,
                    ResolveCallback = Marshal.GetFunctionPointerForDelegate(_resolveCallback),
                    QueryContext = _context,
                });
                _cancel = Allocate(default(DnsServiceCancel));
            }
            catch
            {
                Free(ref _request);
                Free(ref _cancel);
                Free(ref _queryName);
                UnregisterSession(ref _context);
                throw;
            }
        }

        public Task<HueBridge?> Completion => _completion.Task;

        public void Start()
        {
            _pending = true;
            uint status;
            try
            {
                status = DnsServiceResolve(_request, _cancel);
            }
            catch
            {
                _pending = false;
                throw;
            }

            if (IsRequestPending(status)) return;

            _pending = false;
            throw new Win32Exception(
                (int)status,
                $"DNS-SD resolve could not start (status={status}).");
        }

        public void Complete(HueBridge? bridge)
        {
            _pending = false;
            _completion.TrySetResult(bridge);
        }

        public async Task CancelAsync()
        {
            if (!_pending) return;

            var status = DnsServiceResolveCancel(_cancel);
            if (status == ErrorInvalidParameter && _completion.Task.IsCompleted)
            {
                await _completion.Task.ConfigureAwait(false);
                return;
            }
            if (status is not (ErrorSuccess or ErrorCancelled))
            {
                throw new Win32Exception((int)status, "DNS-SD resolve could not be cancelled.");
            }
            await _completion.Task.ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_pending) return;
            Free(ref _request);
            Free(ref _cancel);
            Free(ref _queryName);
            UnregisterSession(ref _context);
        }
    }

}
