namespace Deckle.Anytype;

internal interface IBackendEndpointTrust
{
    bool IsTrusted();
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

    public bool IsTrusted()
    {
        BackendListenerSnapshot snapshot = listener.Inspect();
        if (snapshot.State != BackendListenerState.Owned)
            return false;

        using IBackendProcess? owner = processes.Open(snapshot.ProcessId);
        if (owner is null || owner.HasExited)
            return false;

        try
        {
            string actual = Path.GetFullPath(owner.ExecutablePath);
            return provider.TrustedExecutablePaths().Any(path =>
                string.Equals(Path.GetFullPath(path), actual, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is
            ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
