using System.Diagnostics;

namespace Deckle.Atelier;

public sealed record PlannedRecycle(PhotoFile Photo, IReadOnlyList<string> Sidecars);

// What "Appliquer les marques…" will do, for the summary the view shows
// before anything moves (12.6): "37 photos vont à la Corbeille de Windows ·
// 0,9 Go", "160 photos à garder : rien ne change sur le disque", "215 photos
// sans marque : rien ne change".
//
// RecycleBytes counts the photos, as the board's weight does; their sidecars
// weigh a few kilobytes and go with them.
public sealed record MarksPlan(
    string Folder,
    IReadOnlyList<PlannedRecycle> Recycles,
    long RecycleBytes,
    int KeptUnchanged,
    int Unmarked,
    RenamePlan Renames)
{
    // Nothing to apply: the view disables "Appliquer les marques…" (12.22).
    public bool IsEmpty => Recycles.Count == 0 && Renames.Renames.Count == 0;
}

public sealed record RecycleResult(PhotoFile Photo, RecycleOutcome Outcome);

public sealed record MarksOutcome(IReadOnlyList<RecycleResult> Recycles, IReadOnlyList<RenameResult> Renames)
{
    public int Recycled => Recycles.Count(r => r.Outcome.Status == RecycleStatus.Recycled);
    public int Renamed => Renames.Count(r => r.Status == RenameStatus.Renamed);

    // Every photo that did not get what the plan said, for the view to name.
    public IEnumerable<PhotoFile> NotApplied =>
        Recycles.Where(r => r.Outcome.Status != RecycleStatus.Recycled).Select(r => r.Photo)
            .Concat(Renames.Where(r => r.Status != RenameStatus.Renamed).Select(r => r.Photo));
}

// Applies the marks of a folder in one step that first shows what will happen
// (spec, Journeys and Behaviours). The photos marked "À supprimer" go to the
// Windows Recycle Bin with their darktable sidecars. The kept and unmarked
// photos do not change (12.6), unless the caller asks for the kept ones to be
// renamed by date in the same step: no board draws that option, so it is off
// unless asked (Open question 28).
//
// After the application, the marks of recycled photos leave the store, the
// kept marks stay (12.22 still counts "160 à garder"), and a renamed photo's
// mark follows its name.
public static class MarksApplier
{
    // takenOf reads a kept photo's date taken; only called when renameKept.
    public static MarksPlan Plan(string folder, IReadOnlyList<PhotoFile> photos, PhotoMarkStore marks,
                                 bool renameKept = false, Func<PhotoFile, DateTime?>? takenOf = null)
    {
        var recycles = new List<PlannedRecycle>();
        var kept = new List<PhotoFile>();
        int unmarked = 0;
        long bytes = 0;
        foreach (PhotoFile photo in photos)
        {
            switch (marks.MarkOf(photo.Name))
            {
                case PhotoMark.Delete:
                    recycles.Add(new PlannedRecycle(photo, PhotoSidecars.Of(photo.Path)));
                    bytes += photo.Size;
                    break;
                case PhotoMark.Keep:
                    kept.Add(photo);
                    break;
                default:
                    unmarked++;
                    break;
            }
        }

        RenamePlan renames = renameKept
            ? PhotoRenamer.Plan(folder, kept, takenOf ?? (p => CaptureDataReader.Read(p.Path).Data.Taken))
            : RenamePlan.Empty;
        return new MarksPlan(folder, recycles, bytes, kept.Count - renames.Renames.Count, unmarked, renames);
    }

    // Recycles first, then renames. Progress counts the photos done, out of
    // Recycles.Count + Renames.Count. Blocking work on the disk and the shell:
    // the view runs it off the UI thread, as ExecuteAsync does.
    public static Task<MarksOutcome> ExecuteAsync(MarksPlan plan, PhotoMarkStore marks, IRecycleBin bin,
                                                  IProgress<int>? progress = null, CancellationToken cancel = default) =>
        Task.Run(() => Execute(plan, marks, bin, progress, cancel), CancellationToken.None);

    public static MarksOutcome Execute(MarksPlan plan, PhotoMarkStore marks, IRecycleBin bin,
                                       IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        string runId = Guid.NewGuid().ToString("N")[..8];
        var clock = Stopwatch.StartNew();
        DeckleAtelierSource.Log.MarksApplyStarted(runId, plan.Recycles.Count, plan.Renames.Renames.Count);

        var recycleResults = new RecycleResult[plan.Recycles.Count];
        if (plan.Recycles.Count > 0)
        {
            bin.Recycle([.. plan.Recycles.Select(r => r.Photo.Path)], (i, outcome) =>
            {
                recycleResults[i] = new RecycleResult(plan.Recycles[i].Photo, outcome);
                progress?.Report(i + 1);
            }, cancel);

            // A sidecar goes only with its photo. Its own outcome is not the
            // person's concern: a leftover .xmp is harmless and is logged by
            // the bin itself when it fails.
            string[] sidecars = [.. plan.Recycles
                .Where((r, i) => recycleResults[i]?.Outcome.Status == RecycleStatus.Recycled)
                .SelectMany(r => r.Sidecars)];
            if (sidecars.Length > 0) bin.Recycle(sidecars, (_, _) => { }, CancellationToken.None);
        }

        marks.Settle(recycleResults.Where(r => r?.Outcome.Status == RecycleStatus.Recycled).Select(r => r.Photo.Name), []);

        int offset = plan.Recycles.Count;
        IReadOnlyList<RenameResult> renameResults = PhotoRenamer.Execute(plan.Renames, marks,
            progress is null ? null : new OffsetProgress(progress, offset), cancel);

        var outcome = new MarksOutcome(
            [.. recycleResults.Select((r, i) => r ?? new RecycleResult(plan.Recycles[i].Photo, new RecycleOutcome(RecycleStatus.NotDone)))],
            renameResults);
        DeckleAtelierSource.Log.MarksApplyEnded(runId, outcome.Recycled, outcome.Renamed, outcome.NotApplied.Count(), clock.ElapsedMilliseconds);
        return outcome;
    }

    // Passes the renames' count on, after the recycles, in the same call:
    // Progress<T> would post it later through a context, out of order.
    private sealed class OffsetProgress(IProgress<int> inner, int offset) : IProgress<int>
    {
        public void Report(int value) => inner.Report(offset + value);
    }
}
