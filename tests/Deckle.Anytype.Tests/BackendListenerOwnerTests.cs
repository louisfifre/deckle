using System.Net;
using System.Net.Sockets;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "integration")]
public sealed class BackendListenerOwnerTests
{
    [Fact]
    public void Bound_loopback_listener_reports_its_owning_process()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        BackendListenerSnapshot snapshot = new BackendListenerOwner(port).Inspect();

        Assert.Equal(BackendListenerState.Owned, snapshot.State);
        Assert.Equal(Environment.ProcessId, snapshot.ProcessId);
    }
}
