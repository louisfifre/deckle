using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace Deckle.Lighting;

internal sealed class HueEntertainmentTransport : IHueEntertainmentTransport
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly string _bridgeIp;
    private readonly string _username;
    private readonly string _clientKey;

    private Socket? _socket;
    private HueDatagramTransport? _udp;
    private DtlsTransport? _dtls;
    private bool _disposed;

    public HueEntertainmentTransport(string bridgeIp, string username, string clientKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bridgeIp);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);

        _bridgeIp = bridgeIp;
        _username = username;
        _clientKey = clientKey;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_dtls is not null) return;

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ConnectTimeout);
            await socket.ConnectAsync(IPAddress.Parse(_bridgeIp), 2100, timeout.Token).ConfigureAwait(false);

            _udp = new HueDatagramTransport(socket);
            _socket = socket;

            byte[] psk = HexToBytes(_clientKey);
            var identity = new BasicTlsPskIdentity(_username, psk);
            var crypto = new BcTlsCrypto(new SecureRandom());
            var client = new HueDtlsPskClient(crypto, identity);
            var connectTask = Task.Run(() => new DtlsClientProtocol().Connect(client, _udp));
            using var abortRegistration = timeout.Token.Register(static state =>
            {
                try { ((HueDatagramTransport)state!).Dispose(); } catch { }
            }, _udp);

            try
            {
                _dtls = await connectTask.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _ = connectTask.ContinueWith(
                    static t => _ = t.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
                throw new TimeoutException("Hue Entertainment DTLS handshake timed out.");
            }
        }
        catch
        {
            socket.Dispose();
            _udp?.Dispose();
            _udp = null;
            _socket = null;
            throw;
        }
    }

    public void Send(byte[] payload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_dtls is null)
            throw new InvalidOperationException("Hue Entertainment transport is not connected.");

        _dtls.Send(payload, 0, payload.Length);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _dtls?.Close(); } catch { }
        try { _udp?.Dispose(); } catch { }
        try { _socket?.Dispose(); } catch { }

        _dtls = null;
        _udp = null;
        _socket = null;
    }

    private static byte[] HexToBytes(string hex)
    {
        hex = hex.Replace("-", "", StringComparison.Ordinal);
        if (hex.Length == 0 || hex.Length % 2 != 0)
            throw new ArgumentException("Hue Entertainment client key must be an even-length hex string.", nameof(hex));

        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    private sealed class HueDtlsPskClient : DefaultTlsClient
    {
        private readonly TlsPskIdentity _identity;

        public HueDtlsPskClient(BcTlsCrypto crypto, TlsPskIdentity identity)
            : base(crypto)
        {
            _identity = identity;
        }

        public override ProtocolVersion[] GetProtocolVersions()
            => [ProtocolVersion.DTLSv12];

        public override int[] GetCipherSuites()
            => [CipherSuite.TLS_PSK_WITH_AES_128_GCM_SHA256];

        public override TlsPskIdentity GetPskIdentity()
            => _identity;

        public override TlsAuthentication GetAuthentication()
            => new NoCertificateAuthentication();
    }

    private sealed class NoCertificateAuthentication : TlsAuthentication
    {
        public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
        {
        }

        public TlsCredentials? GetClientCredentials(CertificateRequest certificateRequest)
            => null;
    }

    private sealed class HueDatagramTransport : DatagramTransport, IDisposable
    {
        private readonly Socket _socket;
        private bool _disposed;

        public HueDatagramTransport(Socket socket)
        {
            _socket = socket;
        }

        public int GetReceiveLimit() => 4096;

        public int GetSendLimit() => 4096;

        public int Receive(byte[] buf, int off, int len, int waitMillis)
        {
            if (!_socket.Connected)
                throw new InvalidOperationException("Hue Entertainment UDP socket is not connected.");

            if (!WaitForData(waitMillis))
                return -1;

            return _socket.Receive(buf, off, len, SocketFlags.None);
        }

        public int Receive(Span<byte> buffer, int waitMillis)
        {
            if (!_socket.Connected)
                throw new InvalidOperationException("Hue Entertainment UDP socket is not connected.");

            if (!WaitForData(waitMillis))
                return -1;

            return _socket.Receive(buffer, SocketFlags.None);
        }

        private bool WaitForData(int waitMillis)
        {
            if (waitMillis == 0)
                return _socket.Available > 0;

            int boundedWaitMs = waitMillis < 0 ? 1000 : Math.Min(waitMillis, 1000);
            return _socket.Poll(boundedWaitMs * 1000, SelectMode.SelectRead);
        }

        public void Send(byte[] buf, int off, int len)
            => _socket.Send(buf, off, len, SocketFlags.None);

        public void Send(ReadOnlySpan<byte> buffer)
            => _socket.Send(buffer, SocketFlags.None);

        public void Close() => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            _socket.Dispose();
        }
    }
}
