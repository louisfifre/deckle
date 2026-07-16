using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

[Collection(OperationalLogAdmissionCollection.Name)]
[Trait("Category", "observability")]
public sealed class OperationalLogAdmissionTests : IDisposable
{
    public OperationalLogAdmissionTests()
    {
        OperationalLogAdmission.SetActive(OperationalLogActivity.Windowing, false);
        OperationalLogAdmission.Configure(static _ => false);
    }

    public void Dispose()
    {
        OperationalLogAdmission.SetActive(OperationalLogActivity.Windowing, false);
        OperationalLogAdmission.Configure(static _ => false);
    }

    [Fact]
    public void OwnedDetailRequiresPolicyAndListener()
    {
        using var listener = new TestEventListener("Deckle-Windowing");

        Assert.False(OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Windowing,
            DeckleWindowingSource.Log,
            EventLevel.Verbose,
            (EventKeywords)Keywords.Windowing));

        OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Windowing);

        Assert.True(OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Windowing,
            DeckleWindowingSource.Log,
            EventLevel.Verbose,
            (EventKeywords)Keywords.Windowing));
    }

    [Fact]
    public void ScopedDetailUsesPolicyOnlyWhileActivityIsActive()
    {
        using var listener = new TestEventListener("Deckle-Windowing");

        Assert.True(OperationalLogAdmission.IsScopedDetailEnabled(
            OperationalLogActivity.Windowing,
            DeckleWindowingSource.Log,
            EventLevel.Verbose,
            (EventKeywords)Keywords.Windowing));

        OperationalLogAdmission.SetActive(OperationalLogActivity.Windowing, true);

        Assert.False(OperationalLogAdmission.IsScopedDetailEnabled(
            OperationalLogActivity.Windowing,
            DeckleWindowingSource.Log,
            EventLevel.Verbose,
            (EventKeywords)Keywords.Windowing));
    }
}
