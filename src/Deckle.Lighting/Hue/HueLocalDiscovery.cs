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
    private const uint DnsRequestPending = 997;
    private const uint ErrorSuccess = 0;
    private const uint ErrorInvalidParameter = 87;
    private const uint ErrorCancelled = 1223;
    private const ushort DnsTypePtr = 12;

    private static readonly TimeSpan BrowseWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ResolveWindow = TimeSpan.FromSeconds(2);

    private static readonly DnsServiceBrowseCallback _browseCallback = OnBrowseResult;
    private static readonly DnsServiceResolveComplete _resolveCallback = OnResolveResult;

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

    private static void OnBrowseResult(uint status, nint context, nint recordPointer)
    {
        var session = (BrowseSession?)GCHandle.FromIntPtr(context).Target;
        if (session is null)
        {
            if (recordPointer != 0) DnsRecordListFree(recordPointer, DnsFreeRecordList);
            return;
        }

        try
        {
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
        finally
        {
            if (recordPointer != 0) DnsRecordListFree(recordPointer, DnsFreeRecordList);
        }
    }

    private static void OnResolveResult(uint status, nint context, nint instancePointer)
    {
        var session = (ResolveSession?)GCHandle.FromIntPtr(context).Target;
        if (session is null)
        {
            if (instancePointer != 0) DnsServiceFreeInstance(instancePointer);
            return;
        }

        try
        {
            HueBridge? bridge = null;
            if (status == ErrorSuccess && instancePointer != 0)
            {
                bridge = ReadBridge(instancePointer);
            }
            session.Complete(bridge);
        }
        catch
        {
            session.Complete(null);
        }
        finally
        {
            if (instancePointer != 0) DnsServiceFreeInstance(instancePointer);
        }
    }

    private sealed class BrowseSession : IDisposable
    {
        private readonly ConcurrentDictionary<string, byte> _serviceNames =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly TaskCompletionSource<uint> _stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private GCHandle _selfHandle;
        private nint _queryName;
        private nint _request;
        private nint _cancel;
        private bool _started;

        public IReadOnlyList<string> ServiceNames => _serviceNames.Keys.ToArray();

        public void Start()
        {
            _selfHandle = GCHandle.Alloc(this);
            _queryName = Marshal.StringToHGlobalUni(HueServiceType);
            _request = Allocate(new DnsServiceBrowseRequest
            {
                Version = 1,
                InterfaceIndex = 0,
                QueryName = _queryName,
                BrowseCallback = Marshal.GetFunctionPointerForDelegate(_browseCallback),
                QueryContext = GCHandle.ToIntPtr(_selfHandle),
            });
            _cancel = Allocate(default(DnsServiceCancel));

            var status = DnsServiceBrowse(_request, _cancel);
            if (status != DnsRequestPending)
            {
                Dispose();
                throw new Win32Exception((int)status, "DNS-SD browse could not start.");
            }
            _started = true;
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
            if (_selfHandle.IsAllocated) _selfHandle.Free();
        }
    }

    private sealed class ResolveSession : IDisposable
    {
        private readonly TaskCompletionSource<HueBridge?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private GCHandle _selfHandle;
        private nint _queryName;
        private nint _request;
        private nint _cancel;
        private bool _pending;

        public ResolveSession(string serviceName)
        {
            _selfHandle = GCHandle.Alloc(this);
            _queryName = Marshal.StringToHGlobalUni(serviceName);
            _request = Allocate(new DnsServiceResolveRequest
            {
                Version = 1,
                InterfaceIndex = 0,
                QueryName = _queryName,
                ResolveCallback = Marshal.GetFunctionPointerForDelegate(_resolveCallback),
                QueryContext = GCHandle.ToIntPtr(_selfHandle),
            });
            _cancel = Allocate(default(DnsServiceCancel));
        }

        public Task<HueBridge?> Completion => _completion.Task;

        public void Start()
        {
            var status = DnsServiceResolve(_request, _cancel);
            if (status != DnsRequestPending)
            {
                throw new Win32Exception((int)status, "DNS-SD resolve could not start.");
            }
            _pending = true;
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
            if (_selfHandle.IsAllocated) _selfHandle.Free();
        }
    }

}
