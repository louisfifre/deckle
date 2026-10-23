using System.Diagnostics.Tracing;
using Deckle.Anytype;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Anytype.Tests;

// A program that is not ours holds the Anytype endpoint. Its path embeds the Windows
// user name, so the log says which program it is and never where it lives.
[Trait("Category", "observability")]
public sealed class BackendOwnerPathTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", "regression")]
    public async Task AnUntrustedEndpointOwnerIsNamedByItsFileNameAlone()
    {
        using var listener = new TestEventListener("Deckle-Anytype");
        string executable = Path.GetFullPath("anytype.exe");
        string foreignDirectory = Path.Combine(Path.GetTempPath(), "SomeWindowsUser", "tools");
        string foreign = Path.Combine(foreignDirectory, "other.exe");
        var owner = new FakeBackendProcess(73, foreign);
        var processes = new FakeBackendProcessHost();
        processes.Opened.Add(owner.Id, owner);
        var reconciler = new BackendReconciler(
            new FakeBackendProvider(executable),
            processes,
            new ScriptedBackendListener { Current = new(BackendListenerState.Owned, owner.Id) },
            new FakeBackendHealth(),
            new ImmediateBackendCoordinator(),
            new ControlledBackendTime());

        BackendReconciliationResult result = await reconciler.ReconcileAsync("startup", Ct);

        Assert.Equal(BackendStartOutcome.EndpointConflict, result.Outcome);
        EventWrittenEventArgs conflict = Assert.Single(
            listener.Events, e => e.EventId == DeckleAnytypeSource.EvtBackendEndpointConflictDetail);
        Assert.Equal(
            "other.exe",
            Convert.ToString(conflict.Payload![conflict.PayloadNames!.IndexOf("detail")]));
        Assert.DoesNotContain(
            listener.Events,
            e => e.Payload!.Any(p => Convert.ToString(p)?.Contains("SomeWindowsUser") == true));
    }
}
