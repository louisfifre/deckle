namespace Deckle.Atelier;

public enum RenameSkipReason
{
    // The file holds no date taken: the rule has nothing to start the name with.
    NoDateTaken,
    // The name already follows the rule.
    AlreadyNamed,
}

public enum RenameStatus
{
    Renamed,
    // The photo was gone when its turn came.
    Missing,
    // Something took the new name since the plan was made; nothing moved.
    TargetTaken,
    // The disk refused; the photo and its sidecars keep their names.
    Failed,
    NotDone,
}

// Names inside the photo's folder, not paths: a renaming never moves a file.
public readonly record struct SidecarRename(string From, string To);

public sealed record PlannedRename(PhotoFile Photo, string NewName, IReadOnlyList<SidecarRename> Sidecars);

public sealed record SkippedRename(PhotoFile Photo, RenameSkipReason Reason);

public sealed record RenamePlan(IReadOnlyList<PlannedRename> Renames, IReadOnlyList<SkippedRename> Skipped)
{
    public static readonly RenamePlan Empty = new([], []);
}

public sealed record RenameResult(PhotoFile Photo, string NewName, RenameStatus Status);

// Renames photos by the date they were taken (PhotoRenameRule), in two steps
// like the application of the marks: a plan the person can read, then its
// execution with one result per photo. Several files in one step (spec,
// Behaviours); the same entry point serves the apply step and a command for
// agents.
public static class PhotoRenamer
{
    // takenOf gives each photo's date taken, null when the file holds none.
    // Collisions are resolved against every name in the folder and every name
    // the plan already gave, sidecars included.
    public static RenamePlan Plan(string folder, IEnumerable<PhotoFile> photos, Func<PhotoFile, DateTime?> takenOf)
    {
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string path in Directory.EnumerateFiles(folder)) occupied.Add(Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return RenamePlan.Empty;
        }

        var renames = new List<PlannedRename>();
        var skipped = new List<SkippedRename>();
        foreach (PhotoFile photo in photos.OrderBy(p => p.Name, FolderListing.NameOrder))
        {
            if (takenOf(photo) is not { } taken)
            {
                skipped.Add(new SkippedRename(photo, RenameSkipReason.NoDateTaken));
                continue;
            }

            string wanted = PhotoRenameRule.NameFor(photo.Name, taken);
            if (wanted.Equals(photo.Name, StringComparison.Ordinal))
            {
                skipped.Add(new SkippedRename(photo, RenameSkipReason.AlreadyNamed));
                continue;
            }

            IReadOnlyList<string> sidecars = PhotoSidecars.Of(photo.Path);
            string target = wanted;
            List<SidecarRename> sidecarRenames = SidecarsFor(sidecars, photo.Name, target);
            for (int counter = 2; IsTaken(target, sidecarRenames, photo.Name, occupied); counter++)
            {
                target = PhotoRenameRule.WithCounter(wanted, counter);
                sidecarRenames = SidecarsFor(sidecars, photo.Name, target);
            }

            occupied.Add(target);
            foreach (SidecarRename s in sidecarRenames) occupied.Add(s.To);
            renames.Add(new PlannedRename(photo, target, sidecarRenames));
        }
        return new RenamePlan(renames, skipped);
    }

    // Runs the plan in order. A photo moves first, then its sidecars; if a
    // sidecar cannot follow, what moved is put back, so a photo is never
    // parted from its darktable edits. A photo's mark follows its new name.
    public static IReadOnlyList<RenameResult> Execute(RenamePlan plan, PhotoMarkStore? marks, IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        var results = new List<RenameResult>(plan.Renames.Count);
        var moved = new List<(string From, string To)>();
        foreach (PlannedRename rename in plan.Renames)
        {
            RenameStatus status = cancel.IsCancellationRequested ? RenameStatus.NotDone : RenameOne(rename);
            results.Add(new RenameResult(rename.Photo, rename.NewName, status));
            if (status == RenameStatus.Renamed) moved.Add((rename.Photo.Name, rename.NewName));
            progress?.Report(results.Count);
        }
        marks?.Settle([], moved);
        return results;
    }

    private static RenameStatus RenameOne(PlannedRename rename)
    {
        string folder = Path.GetDirectoryName(rename.Photo.Path)!;
        string target = Path.Combine(folder, rename.NewName);
        if (!File.Exists(rename.Photo.Path)) return RenameStatus.Missing;
        if (File.Exists(target) || rename.Sidecars.Any(s => File.Exists(Path.Combine(folder, s.To))))
        {
            DeckleAtelierSource.Log.PhotoRenameFailed("target-taken", "none");
            return RenameStatus.TargetTaken;
        }

        var done = new Stack<(string From, string To)>();
        try
        {
            File.Move(rename.Photo.Path, target);
            done.Push((rename.Photo.Path, target));
            foreach (SidecarRename s in rename.Sidecars)
            {
                string from = Path.Combine(folder, s.From);
                if (!File.Exists(from)) continue;
                string to = Path.Combine(folder, s.To);
                File.Move(from, to);
                done.Push((from, to));
            }
            return RenameStatus.Renamed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeckleAtelierSource.Log.PhotoRenameFailed("disk", ex.GetType().Name);
            while (done.TryPop(out var step))
            {
                try { File.Move(step.To, step.From); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return RenameStatus.Failed;
        }
    }

    private static List<SidecarRename> SidecarsFor(IReadOnlyList<string> sidecars, string photoName, string newPhotoName) =>
        [.. sidecars.Select(path => Path.GetFileName(path))
                    .Select(name => new SidecarRename(name, PhotoSidecars.Renamed(name, photoName, newPhotoName)))];

    private static bool IsTaken(string target, List<SidecarRename> sidecars, string ownName, HashSet<string> occupied) =>
        (!target.Equals(ownName, StringComparison.OrdinalIgnoreCase) && occupied.Contains(target))
        || sidecars.Any(s => !s.To.Equals(s.From, StringComparison.OrdinalIgnoreCase) && occupied.Contains(s.To));
}
