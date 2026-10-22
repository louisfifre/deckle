using System.Runtime.CompilerServices;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// An exception the nets mark handled or observed no longer ends the process, so one
// that fires on every frame used to write an Error line per occurrence and, on the UI
// thread, wait for a flush each time. The ledger makes it one incident per exception
// type and site.
[Trait("Category", "unit")]
public sealed class SurvivedExceptionLedgerTests
{
    [Fact]
    [Trait("Category", "regression")]
    public void TheSameExceptionFromTheSameSiteOpensOneIncident()
    {
        var ledger = new SurvivedExceptionLedger();

        SurvivedException first = ledger.Note(SurvivedExceptionOrigin.UiThread, Thrown(() => Fails("a")));
        SurvivedException second = ledger.Note(SurvivedExceptionOrigin.UiThread, Thrown(() => Fails("b")));
        SurvivedException third = ledger.Note(SurvivedExceptionOrigin.UiThread, Thrown(() => Fails("c")));

        Assert.True(first.OpensIncident);
        Assert.False(second.OpensIncident);
        Assert.False(third.OpensIncident);
        Assert.Equal([1, 2, 3], [first.Occurrences, second.Occurrences, third.Occurrences]);
    }

    [Fact]
    public void ADifferentTypeSiteOrOriginIsAnotherIncident()
    {
        var ledger = new SurvivedExceptionLedger();
        string ui = SurvivedExceptionOrigin.UiThread;
        ledger.Note(ui, Thrown(() => Fails("x")));

        Assert.True(ledger.Note(ui, Thrown(() => FailsDifferently())).OpensIncident);
        Assert.True(ledger.Note(ui, Thrown(() => OtherSiteFails())).OpensIncident);
        Assert.True(ledger.Note(SurvivedExceptionOrigin.UnobservedTask, Thrown(() => Fails("x"))).OpensIncident);
    }

    [Fact]
    public void TheSiteNamesTheMethodThatThrew()
    {
        var ledger = new SurvivedExceptionLedger();

        SurvivedException plain = ledger.Note(SurvivedExceptionOrigin.UiThread, Thrown(() => Fails("x")));

        Assert.Equal("InvalidOperationException", plain.Type);
        Assert.Equal(nameof(SurvivedExceptionLedgerTests) + "." + nameof(Fails), plain.Site);
    }

    [Fact]
    public async Task AnAsyncMethodIsNamedAfterTheMethodTheAuthorWrote()
    {
        var ledger = new SurvivedExceptionLedger();

        Exception thrown = await Assert.ThrowsAsync<InvalidOperationException>(FailsAfterAwaitAsync);

        Assert.Equal(
            nameof(SurvivedExceptionLedgerTests) + "." + nameof(FailsAfterAwaitAsync),
            ledger.Note(SurvivedExceptionOrigin.UnobservedTask, thrown).Site);
    }

    [Fact]
    public void AnExceptionThatWasNeverThrownHasNoSite()
    {
        var ledger = new SurvivedExceptionLedger();

        SurvivedException made = ledger.Note(SurvivedExceptionOrigin.UiThread, new InvalidOperationException("x"));

        Assert.Equal("(unknown)", made.Site);
    }

    [Fact]
    public void TheCauseIsOneLineAndBounded()
    {
        var ledger = new SurvivedExceptionLedger();

        SurvivedException multiline = ledger.Note(
            SurvivedExceptionOrigin.UiThread,
            new InvalidOperationException("first line\r\n  second line\n\tthird"));
        SurvivedException huge = ledger.Note(
            SurvivedExceptionOrigin.UiThread,
            new ArgumentException(new string('x', 5_000)));

        Assert.Equal("first line second line third", multiline.Message);
        Assert.Equal(SurvivedExceptionLedger.MaxMessageLength + 3, huge.Message.Length);
        Assert.EndsWith("...", huge.Message);
        Assert.DoesNotContain('\n', huge.Message);
    }

    [Fact]
    public void ConcurrentOccurrencesAreAllCountedAndOnlyOneOpensTheIncident()
    {
        var ledger = new SurvivedExceptionLedger();
        Exception ex = Thrown(() => Fails("x"));
        var results = new SurvivedException[200];

        Parallel.For(0, results.Length, i => results[i] = ledger.Note(SurvivedExceptionOrigin.UiThread, ex));

        Assert.Single(results, r => r.OpensIncident);
        Assert.Equal(results.Length, results.Max(r => r.Occurrences));
    }

    private static Exception Thrown(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("The action was expected to throw.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Fails(string message) => throw new InvalidOperationException(message);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FailsDifferently() => throw new ArgumentException("x");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OtherSiteFails() => throw new InvalidOperationException("x");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task FailsAfterAwaitAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("x");
    }
}
