using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "unit")]
public sealed class BackendEndpointTrustTests
{
    [Fact]
    public void Trusted_live_listener_owner_is_accepted()
    {
        string executable = Path.GetFullPath("anytype.exe");
        var owner = new FakeBackendProcess(27, executable);
        var processes = new FakeBackendProcessHost();
        processes.Opened.Add(owner.Id, owner);
        var listener = new ScriptedBackendListener
        {
            Current = new(BackendListenerState.Owned, owner.Id),
        };
        var trust = new BackendEndpointTrust(
            new FakeBackendProvider(executable), processes, listener);

        Assert.Equal(BackendEndpointVerdict.Trusted, trust.Inspect());
        Assert.True(owner.Disposed);
    }

    [Fact]
    public void Listener_owned_by_another_executable_is_rejected()
    {
        string trusted = Path.GetFullPath("anytype.exe");
        var owner = new FakeBackendProcess(31, Path.GetFullPath("other.exe"));
        var processes = new FakeBackendProcessHost();
        processes.Opened.Add(owner.Id, owner);
        var listener = new ScriptedBackendListener
        {
            Current = new(BackendListenerState.Owned, owner.Id),
        };
        var trust = new BackendEndpointTrust(
            new FakeBackendProvider(trusted), processes, listener);

        Assert.Equal(BackendEndpointVerdict.Untrusted, trust.Inspect());
        Assert.True(owner.Disposed);
    }

    [Fact]
    public void Unbound_port_reads_as_not_listening_rather_than_untrusted()
    {
        var trust = new BackendEndpointTrust(
            new FakeBackendProvider(Path.GetFullPath("anytype.exe")),
            new FakeBackendProcessHost(),
            new ScriptedBackendListener { Current = new(BackendListenerState.Unbound, 0) });

        Assert.Equal(BackendEndpointVerdict.NotListening, trust.Inspect());
    }

    // The listener table says nothing reliable about who holds the port, so
    // neither case may read as "nothing listens": that would be a guess.
    [Fact]
    public void Port_with_several_owners_stays_untrusted() =>
        Assert.Equal(BackendEndpointVerdict.Untrusted, InspectWith(BackendListenerState.Ambiguous));

    [Fact]
    public void Unreadable_port_table_stays_untrusted() =>
        Assert.Equal(BackendEndpointVerdict.Untrusted, InspectWith(BackendListenerState.Failed));

    private static BackendEndpointVerdict InspectWith(BackendListenerState state) =>
        new BackendEndpointTrust(
            new FakeBackendProvider(Path.GetFullPath("anytype.exe")),
            new FakeBackendProcessHost(),
            new ScriptedBackendListener { Current = new(state, 0) }).Inspect();
}
