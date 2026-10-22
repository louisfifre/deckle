using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Setup.Tests;

// Pins what the journal keeps of a relocation that finished without part of the
// diagnostics: the level and the pairing id, which a reader filters on.
[Trait("Category", "observability")]
public sealed class DeckleSetupSourceTests
{
    [Fact]
    public void Files_left_behind_by_a_move_are_one_warning_that_carries_the_attempt_id()
    {
        using var listener = new TestEventListener("Deckle-Setup");
        string workId = "w-skipped-test";

        DeckleSetupSource.Log.RelocateFilesSkipped(workId, skipped: 3);

        EventWrittenEventArgs ev = Assert.Single(
            listener.Events, e => e.EventId == DeckleSetupSource.EvtRelocateFilesSkipped);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.Equal(["work_id", "skipped"], ev.PayloadNames);
        Assert.Equal(workId, ev.Payload?[0]);
        Assert.Equal(3, ev.Payload?[1]);
    }
}
