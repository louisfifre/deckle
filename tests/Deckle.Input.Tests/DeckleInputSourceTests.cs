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
    public void KeyboardHostStartFailureCarriesItsCauseInOneJournalWarning()
    {
        using var listener = new TestEventListener("Deckle-Input");

        DeckleInputSource.Log.KeyboardHostStartFailed("Win32Exception", "Access is denied.", 5);

        var warning = Assert.Single(listener.Events);
        Assert.Equal(DeckleInputSource.EvtKeyboardHostStartFailed, warning.EventId);
        Assert.Equal(EventLevel.Warning, warning.Level);
        Assert.True(warning.HasKeyword(Keywords.Lifecycle));
        Assert.Equal(ObservationTags.Journal, warning.Tags & ObservationTags.Journal);
        Assert.Equal("Win32Exception", warning.Payload?[0]);
        Assert.Equal("Access is denied.", warning.Payload?[1]);
        Assert.Equal(5, warning.Payload?[2]);
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AFailedDeviceListingIsAWarningNotANoTouchpadLine()
    {
        using var listener = new TestEventListener("Deckle-Input");

        DeckleInputSource.Log.TouchpadEnumerationFailed("list", 5);

        var warning = Assert.Single(listener.Events);
        Assert.Equal(DeckleInputSource.EvtTouchpadEnumerationFailed, warning.EventId);
        Assert.Equal(EventLevel.Warning, warning.Level);
        Assert.Equal("list", warning.Payload?[0]);
        Assert.Equal(5, warning.Payload?[1]);
    }

    [Fact]
    public void HostLifecycleMilestonesReachTheJournalWithAnOutcome()
    {
        using var listener = new TestEventListener("Deckle-Input");

        DeckleInputSource.Log.HostStarted(1);
        DeckleInputSource.Log.HostStopped("failed", "get_message_failed");

        Assert.Collection(listener.Events,
            started =>
            {
                Assert.Equal(DeckleInputSource.EvtHostStarted, started.EventId);
                Assert.Equal(EventLevel.Informational, started.Level);
                Assert.True(started.HasKeyword(Keywords.Lifecycle));
                Assert.Equal(ObservationTags.Journal, started.Tags & ObservationTags.Journal);
            },
            stopped =>
            {
                Assert.Equal(DeckleInputSource.EvtHostStopped, stopped.EventId);
                Assert.Equal(EventLevel.Informational, stopped.Level);
                Assert.Equal(ObservationTags.Journal, stopped.Tags & ObservationTags.Journal);
                Assert.Equal("failed", stopped.Payload?[0]);
                Assert.Equal("get_message_failed", stopped.Payload?[1]);
            });
    }

    [Fact]
    public void PumpFailureIsAnErrorCarryingItsCause()
    {
        using var listener = new TestEventListener("Deckle-Input");

        DeckleInputSource.Log.InputPumpFailed(
            DeckleInputSource.KeyboardHost, "get_message_failed", 1400, "", "");

        var error = Assert.Single(listener.Events);
        Assert.Equal(DeckleInputSource.EvtInputPumpFailed, error.EventId);
        Assert.Equal(EventLevel.Error, error.Level);
        Assert.Equal("keyboard", error.Payload?[0]);
        Assert.Equal("get_message_failed", error.Payload?[1]);
        Assert.Equal(1400, error.Payload?[2]);
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

    // The settings store reports in free text. A damaged file at load is a
    // Warning (degraded, defaults in use) and a failed write an Error.
    [Fact]
    public void ADamagedSettingsFileSurfacesAsAWarningAndAFailedWriteAsAnError()
    {
        using var listener = new TestEventListener("Deckle-Input");

        DeckleInputSource.Log.SettingsLoadWarning("reason=parse_failed");
        DeckleInputSource.Log.SettingsLoadError("reason=write_failed");

        Assert.Collection(
            listener.Events,
            warning =>
            {
                Assert.Equal(DeckleInputSource.EvtSettingsLoadWarning, warning.EventId);
                Assert.Equal(EventLevel.Warning, warning.Level);
                Assert.Equal("reason=parse_failed", warning.Payload?[0]);
            },
            error =>
            {
                Assert.Equal(DeckleInputSource.EvtSettingsLoadError, error.EventId);
                Assert.Equal(EventLevel.Error, error.Level);
                Assert.Equal("reason=write_failed", error.Payload?[0]);
            });
    }
}
