using System.Net;

namespace Deckle.TestSupport;

/// <summary>
/// Owns an <see cref="HttpListener"/> bound directly to an available ephemeral
/// loopback port. Binding is retried without pre-reserving and releasing the port,
/// so parallel test processes cannot steal it between discovery and use.
/// </summary>
public sealed class LoopbackHttpListenerLease : IDisposable
{
    private const int FirstEphemeralPort = 49152;
    private const int EphemeralPortCount = 16384;
    private const int MaxAttempts = 32;

    private LoopbackHttpListenerLease(HttpListener listener, string prefix)
    {
        Listener = listener;
        Prefix = prefix;
    }

    public HttpListener Listener { get; }
    public string Prefix { get; }

    public static LoopbackHttpListenerLease Start()
    {
        HttpListenerException? lastError = null;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            int port = Random.Shared.Next(FirstEphemeralPort, FirstEphemeralPort + EphemeralPortCount);
            string prefix = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);

            try
            {
                listener.Start();
                return new LoopbackHttpListenerLease(listener, prefix);
            }
            catch (HttpListenerException error)
            {
                lastError = error;
                listener.Close();
            }
        }

        throw new InvalidOperationException(
            $"Could not bind a loopback HTTP listener after {MaxAttempts} attempts.",
            lastError);
    }

    public void Dispose() => Listener.Close();
}
