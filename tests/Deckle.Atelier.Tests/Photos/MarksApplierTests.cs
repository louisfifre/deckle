using Xunit;

namespace Deckle.Atelier.Tests;

// The marks are applied in one step, after a summary: the photos to delete go
// to the Recycle Bin, kept and unmarked photos do not change (12.6, 12.22).
// The bin is the IRecycleBin seam, played here by a stand-in that moves files
// aside, so the step is exercised without the shell.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class MarksApplierTests
{
    [Fact]
    public void ThePlanIsTheSummaryTheDialogShows()
    {
        using var scratch = new ScratchFolder();
        (string shoot, PhotoMarkStore marks) = Shoot(scratch);

        MarksPlan plan = MarksApplier.Plan(shoot, PhotoFolder.List(shoot).Photos, marks);

        Assert.Equal(["b.NEF", "c.NEF"], plan.Recycles.Select(r => r.Photo.Name));
        Assert.Equal(2000 + 3000, plan.RecycleBytes);
        Assert.Equal(1, plan.KeptUnchanged);
        Assert.Equal(1, plan.Unmarked);
        Assert.Empty(plan.Renames.Renames);
        Assert.False(plan.IsEmpty);
    }

    [Fact]
    public void ApplyingRecyclesTheDeletedPhotosWithTheirSidecarsAndNothingElse()
    {
        using var scratch = new ScratchFolder();
        (string shoot, PhotoMarkStore marks) = Shoot(scratch);
        scratch.File("xmp", "Shoot", "b.NEF.xmp");
        var bin = new AsideBin(scratch.Folder("bin"));
        var progress = new List<int>();

        MarksOutcome outcome = MarksApplier.Execute(MarksApplier.Plan(shoot, PhotoFolder.List(shoot).Photos, marks), marks, bin, new SyncProgress(progress));

        Assert.Equal(2, outcome.Recycled);
        Assert.Empty(outcome.NotApplied);
        Assert.Equal(["a.NEF", "d.NEF"], Directory.GetFiles(shoot).Select(Path.GetFileName).Order());
        Assert.Equal(["b.NEF", "b.NEF.xmp", "c.NEF"], Directory.GetFiles(bin.Folder).Select(Path.GetFileName).Order());
        Assert.Equal([1, 2], progress);
    }

    [Fact]
    public void AfterApplyingTheKeptMarksStayAndTheDeletedOnesLeave()
    {
        using var scratch = new ScratchFolder();
        (string shoot, PhotoMarkStore marks) = Shoot(scratch);

        MarksApplier.Execute(MarksApplier.Plan(shoot, PhotoFolder.List(shoot).Photos, marks), marks, new AsideBin(scratch.Folder("bin")));

        Assert.Equal(new MarkCounts(Unmarked: 1, Keep: 1, Delete: 0), marks.Count(PhotoFolder.List(shoot).Photos));
        Assert.True(MarksApplier.Plan(shoot, PhotoFolder.List(shoot).Photos, marks).IsEmpty);
    }

    [Fact]
    public void APhotoTheBinRefusesStaysWithItsMarkAndIsNamed()
    {
        using var scratch = new ScratchFolder();
        (string shoot, PhotoMarkStore marks) = Shoot(scratch);
        var bin = new AsideBin(scratch.Folder("bin")) { Refuse = "c.NEF" };

        MarksOutcome outcome = MarksApplier.Execute(MarksApplier.Plan(shoot, PhotoFolder.List(shoot).Photos, marks), marks, bin);

        Assert.Equal("c.NEF", Assert.Single(outcome.NotApplied).Name);
        Assert.True(File.Exists(Path.Combine(shoot, "c.NEF")));
        Assert.Equal(PhotoMark.Delete, marks.MarkOf("c.NEF"));
    }

    [Fact]
    public void KeptPhotosAreRenamedOnlyWhenAsked()
    {
        using var scratch = new ScratchFolder();
        (string shoot, PhotoMarkStore marks) = Shoot(scratch);
        DateTime taken = new(2026, 6, 14, 15, 42, 7);

        MarksPlan plan = MarksApplier.Plan(shoot, PhotoFolder.List(shoot).Photos, marks, renameKept: true, takenOf: _ => taken);
        MarksOutcome outcome = MarksApplier.Execute(plan, marks, new AsideBin(scratch.Folder("bin")));

        Assert.Equal(0, plan.KeptUnchanged);
        Assert.Equal("2026-06-14_154207_a.NEF", Assert.Single(outcome.Renames).NewName);
        Assert.Equal(["2026-06-14_154207_a.NEF", "d.NEF"], Directory.GetFiles(shoot).Select(Path.GetFileName).Order());
        Assert.Equal(PhotoMark.Keep, marks.MarkOf("2026-06-14_154207_a.NEF"));
    }

    // a kept, b and c to delete, d unmarked; sizes 1000 to 4000 bytes.
    private static (string Shoot, PhotoMarkStore Marks) Shoot(ScratchFolder scratch)
    {
        string shoot = scratch.Folder("Shoot");
        string[] names = ["a.NEF", "b.NEF", "c.NEF", "d.NEF"];
        for (int i = 0; i < names.Length; i++) File.WriteAllBytes(Path.Combine(shoot, names[i]), new byte[(i + 1) * 1000]);
        var marks = new PhotoMarkStore(scratch.Folder("data"), shoot);
        marks.Mark("a.NEF", PhotoMark.Keep);
        marks.Mark(["b.NEF", "c.NEF"], PhotoMark.Delete);
        return (shoot, marks);
    }

    private sealed class AsideBin(string folder) : IRecycleBin
    {
        public string Folder { get; } = folder;
        public string? Refuse { get; init; }

        public void Recycle(IReadOnlyList<string> paths, Action<int, RecycleOutcome> onEach, CancellationToken cancel)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                if (Path.GetFileName(paths[i]) == Refuse)
                {
                    onEach(i, new RecycleOutcome(RecycleStatus.Refused));
                    continue;
                }
                string to = Path.Combine(Folder, Path.GetFileName(paths[i]));
                File.Move(paths[i], to);
                onEach(i, new RecycleOutcome(RecycleStatus.Recycled, 0, to));
            }
        }
    }

    private sealed class SyncProgress(List<int> seen) : IProgress<int>
    {
        public void Report(int value) => seen.Add(value);
    }
}
