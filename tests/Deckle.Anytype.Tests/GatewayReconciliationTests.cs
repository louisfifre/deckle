using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "unit")]
public sealed class GatewayReconciliationTests
{
    private const string Versions = @"C:\Providers\Anytype Gateway\versions";
    private static readonly string Expected = Path.Combine(Versions, "current", GatewayContract.ExecutableName);

    [Fact]
    public void The_same_build_already_listening_is_adopted()
    {
        GatewayDecision decision = Decide(Owned(42), Expected.ToUpperInvariant());

        Assert.Equal(GatewayAction.Adopt, decision.Action);
        Assert.Equal(42, decision.ProcessId);
    }

    [Fact]
    public void Another_build_listening_is_replaced()
    {
        GatewayDecision decision = Decide(Owned(42), Path.Combine(Versions, "older", GatewayContract.ExecutableName));

        Assert.Equal(GatewayAction.Replace, decision.Action);
        Assert.Equal(42, decision.ProcessId);
    }

    [Fact]
    public void A_foreign_program_on_the_port_is_a_conflict_and_is_left_alone()
    {
        GatewayDecision decision = Decide(Owned(42), @"C:\Programs\Deckle\Deckle.exe");

        Assert.Equal(GatewayAction.Conflict, decision.Action);
        Assert.Equal("foreign_owner", decision.Reason);
    }

    [Fact]
    public void An_owner_whose_image_cannot_be_read_is_a_conflict()
    {
        Assert.Equal("owner_unreadable", Decide(Owned(42), ownerPath: null).Reason);
        Assert.Equal("owner_unreadable", Decide(new(BackendListenerState.Failed), null).Reason);
        Assert.Equal("ambiguous_owner", Decide(new(BackendListenerState.Ambiguous), null).Reason);
    }

    [Fact]
    public void A_free_port_means_start_this_build()
    {
        Assert.Equal(GatewayAction.Spawn, Decide(new(BackendListenerState.Unbound), null).Action);
    }

    private static BackendListenerSnapshot Owned(int pid) => new(BackendListenerState.Owned, pid);

    private static GatewayDecision Decide(BackendListenerSnapshot listener, string? ownerPath) =>
        GatewayReconciliation.Decide(listener, ownerPath, Expected, Versions);
}
