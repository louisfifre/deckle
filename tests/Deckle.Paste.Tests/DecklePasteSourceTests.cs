using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Paste.Tests;

// The provider says how each paste ended, never what was pasted nor where.
// Each test subscribes its own listener on the provider singleton, so every
// suite that emits on it shares this collection.
[Collection("paste provider singleton")]
[Trait("Category", "observability")]
public sealed class DecklePasteSourceTests
{
    private const string Secret = "my private note";

    [Fact]
    public void ASavedPasteIsOneMilestoneThenItsMeasuresWithoutTheText()
    {
        using var scratch = new ScratchFolder();
        string folder = scratch.Folder("out");
        using var listener = new TestEventListener("Deckle-Paste");

        new PasteSaver().Save(new PastedText(Secret), folder);

        Assert.Equal(
            [DecklePasteSource.EvtPasteSaved, DecklePasteSource.EvtPasteSavedDetail],
            listener.Events.Select(e => e.EventId));
        EventWrittenEventArgs saved = listener.Events[0];
        Assert.Equal(EventLevel.Informational, saved.Level);
        Assert.True(Deckle.Diagnostics.ObservationTags.IsMilestone(saved.Tags));
        Assert.Equal("Text", saved.Payload?[0]);
        Assert.Equal(1, listener.Events[1].Payload?[1]);
        Assert.Equal((long)Secret.Length, listener.Events[1].Payload?[2]);
        AssertCarriesNothingOf(listener.Events, Secret, folder);
    }

    [Fact]
    public void AFailedPasteIsOneWarningWithItsReason()
    {
        using var scratch = new ScratchFolder();
        string gone = Path.Combine(scratch.Path, "gone");
        using var listener = new TestEventListener("Deckle-Paste");

        new PasteSaver().Save(new PastedText(Secret), gone);

        EventWrittenEventArgs failed = Assert.Single(listener.Events);
        Assert.Equal(DecklePasteSource.EvtPasteSaveFailed, failed.EventId);
        Assert.Equal(EventLevel.Warning, failed.Level);
        Assert.Equal("Text", failed.Payload?[0]);
        Assert.Equal("FolderMissing", failed.Payload?[1]);
        AssertCarriesNothingOf(listener.Events, Secret, gone);
    }

    [Fact]
    public void AWriteFailureCarriesTheErrorType()
    {
        using var scratch = new ScratchFolder();
        string album = scratch.Folder("Album");
        using var listener = new TestEventListener("Deckle-Paste");

        new PasteSaver().Save(new PastedFiles([album]), album);

        EventWrittenEventArgs failed = Assert.Single(listener.Events);
        Assert.Equal("WriteFailed", failed.Payload?[1]);
        Assert.Equal("IOException", failed.Payload?[3]);
        AssertCarriesNothingOf(listener.Events, album);
    }

    [Fact]
    public void ChoosingAFolderSaysSoWithoutThePath()
    {
        using var scratch = new ScratchFolder();
        string chosen = scratch.Folder("Private");
        var store = new PasteFolderStore(scratch.Folder("data"));
        using var listener = new TestEventListener("Deckle-Paste");

        store.SetFolder(chosen);

        EventWrittenEventArgs ev = Assert.Single(listener.Events);
        Assert.Equal(DecklePasteSource.EvtFolderChosen, ev.EventId);
        AssertCarriesNothingOf(listener.Events, chosen);
    }

    [Fact]
    public void AnUnreadableFolderFileIsOneWarning()
    {
        using var scratch = new ScratchFolder();
        string data = scratch.Folder("data");
        File.WriteAllText(Path.Combine(data, PasteFolderStore.FileName), "{ not json");
        using var listener = new TestEventListener("Deckle-Paste");

        _ = new PasteFolderStore(data);

        EventWrittenEventArgs ev = Assert.Single(listener.Events);
        Assert.Equal(DecklePasteSource.EvtFolderLoadFailed, ev.EventId);
        Assert.Equal(EventLevel.Warning, ev.Level);
    }

    // A paste that stopped in the window, before the saver, left no event.
    [Fact]
    [Trait("Category", "regression")]
    public void AnUnreadableClipboardIsOneWarningWithTheErrorTypeAndCode()
    {
        using var listener = new TestEventListener("Deckle-Paste");

        PasteWindowLog.ClipboardUnreadable(new System.Runtime.InteropServices.COMException(Secret, unchecked((int)0x800401D0)));

        EventWrittenEventArgs ev = Assert.Single(listener.Events);
        Assert.Equal(DecklePasteSource.EvtPasteReadFailed, ev.EventId);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.Equal("ClipboardUnreadable", ev.Payload?[0]);
        Assert.Equal("COMException", ev.Payload?[1]);
        Assert.Equal(unchecked((int)0x800401D0), ev.Payload?[2]);
        AssertCarriesNothingOf(listener.Events, Secret);
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AClipboardThatHeldNothingIsOneWarningWithItsReason()
    {
        using var listener = new TestEventListener("Deckle-Paste");

        PasteWindowLog.NothingToSave();

        EventWrittenEventArgs ev = Assert.Single(listener.Events);
        Assert.Equal(DecklePasteSource.EvtPasteReadFailed, ev.EventId);
        Assert.Equal("NothingToSave", ev.Payload?[0]);
    }

    // A folder picker that did not open left the click without effect and
    // the log without a trace.
    [Fact]
    [Trait("Category", "regression")]
    public void AFolderPickerThatDidNotOpenIsOneWarning()
    {
        using var listener = new TestEventListener("Deckle-Paste");

        PasteWindowLog.FolderPickerFailed(new System.Runtime.InteropServices.COMException(Secret, unchecked((int)0x80004005)));

        EventWrittenEventArgs ev = Assert.Single(listener.Events);
        Assert.Equal(DecklePasteSource.EvtFolderPickerFailed, ev.EventId);
        Assert.Equal(EventLevel.Warning, ev.Level);
        Assert.Equal("COMException", ev.Payload?[0]);
        AssertCarriesNothingOf(listener.Events, Secret);
    }

    private static void AssertCarriesNothingOf(IEnumerable<EventWrittenEventArgs> events, params string[] secrets)
    {
        foreach (EventWrittenEventArgs e in events)
        foreach (object? value in e.Payload ?? [])
        foreach (string secret in secrets)
            Assert.DoesNotContain(secret, value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
