using static Deckle.Hosting.SelfCheck.SelfCheckCapture;
using Deckle.Hosting.SelfCheck;
using Deckle.Folders.Views;
using Deckle.Vision;

namespace Deckle.Folders.App;

// The self-check's walk of the folder groups, called last from WalkFoldersAsync,
// once the added folders' walk has left the sample's folders in the pane. It
// makes the group "Work" the way the dialog's Create does, moves two folders
// into it the way the radio item of a folder's menu does, and photographs the
// pane with the group expanded, with it collapsed (and kept so across the
// rail), Home with its section, the pane as the rail, the "Add…" menu, the
// group dialog with a name typed, and the menu of a grouped folder. Then it
// removes the group, so the folders are in no group again.
//
// Nothing outside the sample folder is added or changed.
internal static partial class FoldersSelfCheck
{
    private static async Task WalkFoldersGroupsAsync(
        SelfCheckReport report, InterfaceCaptureRun run, string outputFolder, Action<string> step,
        FoldersWindow window, string sample, SelfCheckWalkGate gate)
    {
        gate.ThrowIfDone();

        async Task Capture(string state, Func<Task> reach, bool keepPopups = false)
        {
            gate.ThrowIfDone();
            bool photographed = gate.Photographs(state);
            step($"folders/{state}");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await reach();
                await Task.Delay(400);
            }
            catch (Exception ex)
            {
                report.Surfaces.Add(new SelfCheckSurface("folders", state, null, false, clock.ElapsedMilliseconds, null, 0, 0,
                    "window-capture", Error: $"{ex.GetType().Name}: {ex.Message}"));
                report.Save(outputFolder);
                return;
            }
            if (!photographed) return;
            (int closed, int left) = keepPopups ? (0, 0) : await CloseTransientPopupsAsync(window);
            report.Surfaces.Add(await CaptureWindowFrameAsync(
                run, window, "folders", state, null, true, clock.ElapsedMilliseconds, closed, left));
            report.Save(outputFolder);
        }

        static void Check(bool holds, string what)
        {
            if (!holds) throw new InvalidOperationException(what);
        }

        void CheckPaneOrder()
        {
            if (window.PaneOrderMismatchForReview() is { } mismatch) throw new InvalidOperationException(mismatch);
        }

        string documents = Path.Combine(sample, "Documents");
        string projects = Path.Combine(sample, "Projets");
        string groupId = "";

        // Two folders move into "Work"; Documents, added last, is the current
        // place, so the pane selects it inside the group.
        await Capture("groups-pane", async () =>
        {
            await window.AddFolderForReviewAsync(projects);
            await window.AddFolderForReviewAsync(documents);
            groupId = window.CreateGroupForReview("Work");
            Check(window.MoveFolderToGroupForReview(projects, groupId), "The Projets folder did not move into the group.");
            Check(window.MoveFolderToGroupForReview(documents, groupId), "The Documents folder did not move into the group.");
            window.SetPaneOpenForReview(true);
            await Task.Delay(300);
            CheckPaneOrder();
            Check(window.IsGroupExpandedForReview(groupId), "The new group does not start expanded.");
            Check(window.CurrentFolderForReview is { } now && string.Equals(now, documents, StringComparison.OrdinalIgnoreCase),
                "The window does not stand in the Documents folder.");
            string? tip = window.GroupTooltipForReview(groupId);
            Check(tip == "Work group: Projets, Documents", $"The group's tooltip reads \"{tip}\".");
        });

        // Collapsed by a click on the group, and still collapsed once the pane
        // has gone to the rail and come back: a sync or a reduction does not
        // open it again.
        await Capture("groups-collapsed", async () =>
        {
            window.SetGroupExpandedForReview(groupId, false);
            await Task.Delay(300);
            window.SetPaneOpenForReview(false);
            await Task.Delay(300);
            window.SetPaneOpenForReview(true);
            await Task.Delay(300);
            Check(!window.IsGroupExpandedForReview(groupId), "The collapsed group opened again with the pane.");
            await window.AddFolderForReviewAsync(documents);
            Check(!window.IsGroupExpandedForReview(groupId), "The collapsed group opened again at a sync.");
        });
        window.SetGroupExpandedForReview(groupId, true);

        await Capture("groups-home", async () =>
        {
            window.NavigateHomeForReview();
            Check(window.AtHomeForReview, "The window does not stand at Home.");
            await Task.Delay(300);
        });

        await Capture("groups-rail", async () =>
        {
            Check(window.OpenHomeTileForReview(documents), "The Home page has no tile for the Documents folder.");
            window.SetPaneOpenForReview(false);
            await Task.Delay(300);
            CheckPaneOrder();
        });

        await Capture("add-menu", async () =>
        {
            window.SetPaneOpenForReview(true);
            await Task.Delay(300);
            IReadOnlyList<string> labels = window.ShowAddMenuForReview();
            Check(labels.Count == 2, $"The Add menu holds {labels.Count} items, not 2.");
            await Task.Delay(300);
        }, keepPopups: true);
        window.HideFolderMenuForReview();

        await Capture("group-dialog", async () =>
        {
            Check(await window.ShowGroupDialogForReviewAsync("Clients"), "Create is not enabled for a free name.");
            await Task.Delay(300);
        }, keepPopups: true);
        window.CloseGroupDialogForReview();
        await Task.Delay(300);

        await Capture("folder-menu-groups", async () =>
        {
            IReadOnlyList<string> labels = window.ShowFolderMenuForReview(documents);
            Check(labels.Count == 4 && labels.Contains("Group"), $"The folder's menu holds [{string.Join(", ", labels)}].");
            await Task.Delay(300);
        }, keepPopups: true);
        window.HideFolderMenuForReview();
        await Task.Delay(300);

        // The group goes; its folders stay, in no group, and the pane follows.
        window.RemoveGroupForReview(groupId);
        CheckPaneOrder();
        Check(window.GroupTooltipForReview(groupId) is null, "The removed group is still in the pane.");
        window.SetPaneOpenForReview(false);
    }
}
