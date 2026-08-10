using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

public sealed class BackendReconciliationLeaseTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Abandoned_lease_is_acquired_and_reinspected()
    {
        string name = $"Deckle.Anytype.Tests.{Guid.NewGuid():N}";
        var options = new NamedWaitHandleOptions
        {
            CurrentUserOnly = true,
            CurrentSessionOnly = false,
        };
        using var keeper = new Mutex(false, name, options);
        using var owned = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var abandoned = new Mutex(false, name, options);
            abandoned.WaitOne();
            owned.Set();
            // Intentionally neither release nor dispose. Thread termination is
            // the crash boundary the production lease must recover from.
        });
        thread.Start();
        owned.Wait(Ct);
        thread.Join();

        bool observedAbandoned = false;
        var lease = new BackendReconciliationLease(name);
        BackendReconciliationResult result = await lease.RunAsync(
            "test",
            abandoned =>
            {
                observedAbandoned = abandoned;
                return new(
                    BackendStartOutcome.NotProvisioned, null,
                    "test", "not-provisioned");
            },
            Ct);

        Assert.True(observedAbandoned);
        Assert.Equal(BackendStartOutcome.NotProvisioned, result.Outcome);
    }
}
