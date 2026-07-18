using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Input;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Input.Tests;

[Trait("Category", "observability")]
public class DeckleInputSourceTests : IDisposable
{
    public DeckleInputSourceTests()
    {
        OperationalLogAdmission.Configure(static _ => false);
        OperationalLogAdmission.SetActive(OperationalLogActivity.Autocorrect, false);
    }

    public void Dispose()
    {
        OperationalLogAdmission.Configure(static _ => false);
        OperationalLogAdmission.SetActive(OperationalLogActivity.Autocorrect, false);
    }

    [Fact]
    public void KeyboardHostStartFailureSeparatesWarningFromVerboseDetail()
    {
        using var listener = new TestEventListener("Deckle-Input");

        DeckleInputSource.Log.KeyboardHostStartFailed();
        DeckleInputSource.Log.KeyboardHostStartFailedDetail("Win32Exception", "Access is denied.");

        Assert.Collection(listener.Events,
            warning =>
            {
                Assert.Equal(DeckleInputSource.EvtKeyboardHostStartFailed, warning.EventId);
                Assert.Equal(EventLevel.Warning, warning.Level);
                Assert.True(warning.HasKeyword(Keywords.Lifecycle));
                Assert.Equal(0, warning.Payload?.Count ?? 0);
            },
            detail =>
            {
                Assert.Equal(DeckleInputSource.EvtKeyboardHostStartFailedDetail, detail.EventId);
                Assert.Equal(EventLevel.Verbose, detail.Level);
                Assert.True(detail.HasKeyword(Keywords.Lifecycle));
                Assert.Equal("Win32Exception", detail.Payload?[0]);
                Assert.Equal("Access is denied.", detail.Payload?[1]);
            });
    }

    [Fact]
    public void KeyboardRollupIsRejectedWhileAutocorrectDetailIsDisabled()
    {
        OperationalLogAdmission.Configure(_ => false);
        OperationalLogAdmission.SetActive(OperationalLogActivity.Autocorrect, true);
        try
        {
            using var listener = new TestEventListener("Deckle-Input");

            DeckleInputSource.Log.KeyboardRollup(20, 1, 2, 3, 4);

            Assert.Empty(listener.Events);
        }
        finally
        {
            OperationalLogAdmission.SetActive(OperationalLogActivity.Autocorrect, false);
        }
    }

    [Fact]
    public void FrameRollupFollowsInputActivityWhilePresenceRemainsAdmitted()
    {
        using var listener = new TestEventListener("Deckle-Input");

        DeckleInputSource.Log.FrameRollup(10, 100, 2, 3, 0, 0, 0, 0);
        DeckleInputSource.Log.TouchpadAbsent();

        Assert.Single(listener.Events);
        Assert.Equal(DeckleInputSource.EvtTouchpadAbsent, listener.Events[0].EventId);

        OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Input);
        DeckleInputSource.Log.FrameRollup(10, 100, 2, 3, 0, 0, 0, 0);

        Assert.Equal(2, listener.Events.Count);
        Assert.Equal(DeckleInputSource.EvtFrameRollup, listener.Events[1].EventId);
    }
}
