using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Atelier.Tests;

// The photo tool's events say what happened without carrying a name, a path
// or a value read in a photo (spec, Behaviours: no photo metadata leaves the
// machine, and the log travels).
[Collection("atelier provider singleton")]
[Trait("Category", "observability")]
public sealed class PhotosEventsTests
{
    [Fact]
    public void ApplyingTheMarksIsAPairedStartAndEndWithCounts()
    {
        using var scratch = new ScratchFolder();
        string shoot = scratch.Folder("Shoot");
        scratch.File("x", "Shoot", "a.NEF");
        var marks = new PhotoMarkStore(scratch.Folder("data"), shoot);
        marks.Mark("a.NEF", PhotoMark.Delete);
        MarksPlan plan = MarksApplier.Plan(shoot, PhotoFolder.List(shoot).Photos, marks);
        using var listener = new TestEventListener("Deckle-Atelier");

        MarksApplier.Execute(plan, marks, new RefusingBin());

        EventWrittenEventArgs start = Assert.Single(listener.Events, e => e.EventId == DeckleAtelierSource.EvtMarksApplyStarted);
        EventWrittenEventArgs end = Assert.Single(listener.Events, e => e.EventId == DeckleAtelierSource.EvtMarksApplyEnded);
        Assert.Equal(start.Payload?[0], end.Payload?[0]);
        Assert.Equal(1, start.Payload?[1]);
        Assert.Equal(0, end.Payload?[1]);
        Assert.Equal(1, end.Payload?[3]);
        AssertNoPathOrName(listener, scratch.Path, "a.NEF");
    }

    [Fact]
    public void AnUnreadablePhotoLogsItsExtensionAndNeverItsName()
    {
        using var scratch = new ScratchFolder();
        string path = scratch.File("not a raw", "DSC_secret.NEF");
        using var listener = new TestEventListener("Deckle-Atelier");

        CaptureDataReader.Read(path);

        EventWrittenEventArgs ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleAtelierSource.EvtCaptureDataReadFailed, ev.EventId);
        Assert.Equal(".nef", ev.Payload?[0]);
        AssertNoPathOrName(listener, scratch.Path, "DSC_secret");
    }

    private static void AssertNoPathOrName(TestEventListener listener, string root, string name)
    {
        foreach (EventWrittenEventArgs e in listener.Events)
        {
            foreach (object? value in e.Payload ?? [])
            {
                string text = value?.ToString() ?? "";
                Assert.DoesNotContain(root, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(name, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private sealed class RefusingBin : IRecycleBin
    {
        public void Recycle(IReadOnlyList<string> paths, Action<int, RecycleOutcome> onEach, CancellationToken cancel)
        {
            for (int i = 0; i < paths.Count; i++) onEach(i, new RecycleOutcome(RecycleStatus.Refused));
        }
    }
}
