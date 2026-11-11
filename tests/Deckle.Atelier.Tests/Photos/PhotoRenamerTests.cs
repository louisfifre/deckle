using Xunit;

namespace Deckle.Atelier.Tests;

// A renamed photo starts with the date it was taken, year then month then day.
[Trait("Category", "unit")]
public sealed class PhotoRenameRuleTests
{
    private static readonly DateTime Taken = new(2026, 6, 14, 15, 42, 7);

    [Fact]
    public void TheNameStartsWithTheDateAndTimeThenTheOriginalName() =>
        Assert.Equal("2026-06-14_154207_DSC_0217.NEF", PhotoRenameRule.NameFor("DSC_0217.NEF", Taken));

    [Fact]
    public void ANameAlreadyDatedIsNeverPrefixedTwice() =>
        Assert.Equal("2026-06-14_154207_DSC_0217.NEF", PhotoRenameRule.NameFor("2025-01-01_000000_DSC_0217.NEF", Taken));

    [Fact]
    public void NamesSortByTimeAcrossCameras()
    {
        string[] names =
        [
            PhotoRenameRule.NameFor("IMG_9001.HEIC", Taken.AddMinutes(1)),
            PhotoRenameRule.NameFor("DSC_0217.NEF", Taken),
            PhotoRenameRule.NameFor("IMG_8999.HEIC", Taken.AddDays(-1)),
        ];

        Assert.Equal(["IMG_8999", "DSC_0217", "IMG_9001"],
            names.Order(StringComparer.Ordinal).Select(n => Path.GetFileNameWithoutExtension(PhotoRenameRule.WithoutDatePrefix(n))));
    }

    [Fact]
    public void ACounterGoesBeforeTheExtension() =>
        Assert.Equal("2026-06-14_154207_DSC_0217-2.NEF", PhotoRenameRule.WithCounter("2026-06-14_154207_DSC_0217.NEF", 2));
}

[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class PhotoRenamerTests
{
    private static readonly DateTime Taken = new(2026, 6, 14, 15, 42, 7);

    [Fact]
    public void APhotoAndItsDarktableSidecarsTakeTheDatedName()
    {
        using var scratch = new ScratchFolder();
        string shoot = scratch.Folder("Shoot");
        scratch.File("raw", "Shoot", "DSC_0217.NEF");
        scratch.File("xmp", "Shoot", "DSC_0217.NEF.xmp");
        scratch.File("xmp", "Shoot", "DSC_0217_01.NEF.xmp");
        scratch.File("other", "Shoot", "DSC_0217.xmp");

        RenamePlan plan = PhotoRenamer.Plan(shoot, PhotoFolder.List(shoot).Photos, _ => Taken);
        IReadOnlyList<RenameResult> results = PhotoRenamer.Execute(plan, marks: null);

        Assert.Equal(RenameStatus.Renamed, Assert.Single(results).Status);
        Assert.Equal(
            ["2026-06-14_154207_DSC_0217.NEF", "2026-06-14_154207_DSC_0217.NEF.xmp", "2026-06-14_154207_DSC_0217_01.NEF.xmp", "DSC_0217.xmp"],
            Directory.GetFiles(shoot).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void TwoPhotosOfTheSameNameAndSecondDoNotCollide()
    {
        using var scratch = new ScratchFolder();
        string shoot = scratch.Folder("Shoot");
        scratch.File("x", "Shoot", "2026-06-14_154207_DSC_0217.NEF");
        scratch.File("x", "Shoot", "DSC_0217.NEF");

        RenamePlan plan = PhotoRenamer.Plan(shoot, PhotoFolder.List(shoot).Photos, _ => Taken);

        PlannedRename rename = Assert.Single(plan.Renames);
        Assert.Equal("2026-06-14_154207_DSC_0217-2.NEF", rename.NewName);
        Assert.Equal(RenameSkipReason.AlreadyNamed, Assert.Single(plan.Skipped).Reason);
    }

    [Fact]
    public void APhotoWithoutADateTakenKeepsItsName()
    {
        using var scratch = new ScratchFolder();
        string shoot = scratch.Folder("Shoot");
        scratch.File("x", "Shoot", "scan.jpg");

        RenamePlan plan = PhotoRenamer.Plan(shoot, PhotoFolder.List(shoot).Photos, _ => null);

        Assert.Empty(plan.Renames);
        Assert.Equal(RenameSkipReason.NoDateTaken, Assert.Single(plan.Skipped).Reason);
    }

    [Fact]
    public void ADateReadFromTheFileNamesIt()
    {
        using var scratch = new ScratchFolder();
        string shoot = scratch.Folder("Shoot");
        new ExifSample().WriteJpeg(Path.Combine(shoot, "DSC_0217.JPG"));

        RenamePlan plan = PhotoRenamer.Plan(shoot, PhotoFolder.List(shoot).Photos, p => CaptureDataReader.Read(p.Path).Data.Taken);

        Assert.Equal("2026-06-14_154207_DSC_0217.JPG", Assert.Single(plan.Renames).NewName);
    }

    [Fact]
    public void ANameTakenSinceThePlanLeavesThePhotoAsItWas()
    {
        using var scratch = new ScratchFolder();
        string shoot = scratch.Folder("Shoot");
        scratch.File("x", "Shoot", "DSC_0217.NEF");
        RenamePlan plan = PhotoRenamer.Plan(shoot, PhotoFolder.List(shoot).Photos, _ => Taken);
        scratch.File("other", "Shoot", "2026-06-14_154207_DSC_0217.NEF");

        RenameResult result = Assert.Single(PhotoRenamer.Execute(plan, marks: null));

        Assert.Equal(RenameStatus.TargetTaken, result.Status);
        Assert.True(File.Exists(Path.Combine(shoot, "DSC_0217.NEF")));
    }

    [Fact]
    public void AMarkFollowsItsPhotoToTheNewName()
    {
        using var scratch = new ScratchFolder();
        string shoot = scratch.Folder("Shoot");
        scratch.File("x", "Shoot", "DSC_0217.NEF");
        var marks = new PhotoMarkStore(scratch.Folder("data"), shoot);
        marks.Mark("DSC_0217.NEF", PhotoMark.Keep);

        PhotoRenamer.Execute(PhotoRenamer.Plan(shoot, PhotoFolder.List(shoot).Photos, _ => Taken), marks);

        Assert.Equal(PhotoMark.Keep, marks.MarkOf("2026-06-14_154207_DSC_0217.NEF"));
        Assert.Equal(PhotoMark.None, marks.MarkOf("DSC_0217.NEF"));
    }
}
