using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

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

        Assert.True(trust.IsTrusted());
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

        Assert.False(trust.IsTrusted());
        Assert.True(owner.Disposed);
    }
}
