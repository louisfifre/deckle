namespace Deckle.Anytype;

// What the guard proved about the fixed endpoint right now. Only Trusted lets a
// request go out; the other two both refuse, but they tell the person different
// things: NotListening is a backend that is not serving yet (nothing untrusted
// is there), Untrusted is something that must not receive the bearer.
internal enum BackendEndpointVerdict
{
    Trusted,
    NotListening,
    Untrusted,
}

internal interface IBackendEndpointTrust
{
    BackendEndpointVerdict Inspect();
}

// Re-proves the identity boundary immediately before a credentialed REST call.
// Supervision establishes liveness over time; this guard prevents a process
// that later takes the fixed port from receiving Deckle's Anytype bearer.
internal sealed class BackendEndpointTrust(
    IBackendProviderCatalog provider,
    IBackendProcessHost processes,
    IBackendListenerOwner listener) : IBackendEndpointTrust
{
    internal static BackendEndpointTrust CreateDefault(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsLoopback || endpoint.Scheme != Uri.UriSchemeHttp)
            throw new InvalidOperationException(
                "A supervised Anytype endpoint must be a local HTTP listener.");

        return new(
            BackendInstallation.ProviderCatalog,
            new BackendProcessHost(),
            new BackendListenerOwner(endpoint.Port));
    }

    public BackendEndpointVerdict Inspect()
    {
        BackendListenerSnapshot snapshot = listener.Inspect();

        // Only an unbound port reads as "nothing listens". An unreadable table
        // or several owners prove nothing about who holds the port, so they
        // stay refused as Untrusted: the bearer goes out on a proof, never on
        // a guess.
        if (snapshot.State == BackendListenerState.Unbound)
            return BackendEndpointVerdict.NotListening;
        if (snapshot.State != BackendListenerState.Owned)
            return BackendEndpointVerdict.Untrusted;

        using IBackendProcess? owner = processes.Open(snapshot.ProcessId);
        if (owner is null || owner.HasExited)
            return BackendEndpointVerdict.Untrusted;

        try
        {
            string actual = Path.GetFullPath(owner.ExecutablePath);
            return provider.TrustedExecutablePaths().Any(path =>
                string.Equals(Path.GetFullPath(path), actual, StringComparison.OrdinalIgnoreCase))
                ? BackendEndpointVerdict.Trusted
                : BackendEndpointVerdict.Untrusted;
        }
        catch (Exception ex) when (ex is
            ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return BackendEndpointVerdict.Untrusted;
        }
    }
}
