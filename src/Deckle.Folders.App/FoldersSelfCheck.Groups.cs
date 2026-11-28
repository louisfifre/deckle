using static Deckle.Hosting.SelfCheck.SelfCheckCapture;
using Deckle.Hosting.SelfCheck;
using Deckle.Folders.Views;
using Deckle.Vision;

namespace Deckle.Folders.App;

// The self-check's walk of the folder groups, called last from WalkFoldersAsync,
// once the added folders' walk has left the sample's folders in the pane. It
// makes the group "Work" the way the dialog's Create does and moves two folders
// into it the way the radio item of a folder's menu does, to reach its first
// state. The three gestures of the groups are then pressed through their
// controls, as a click presses them (FoldersWindow.Gestures): the group folded
// by a click on its row and unfolded by its chevron, each kept across the rail;
// the group's menu opened by its "..."; the folder's menu opened by its "...",
// its "Group" submenu opened and a choice made in it. After each, the walk
// reads what the window shows: the folders drawn or not, the entries drawn and
// enabled, the folder's group in the store and in the pane. It photographs
// these states, Home with its section, the pane as the rail, the "Add…" menu
// and the group dialog with a name typed. Then the group's menu removes the
// group, so the folders are in no group again.
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

        // Each named entry is in the menu, drawn and enabled.
        static void CheckEntries(IReadOnlyList<ReviewMenuEntry> entries, string menu, params string[] ids)
        {
            Check(entries.Count > 0, $"{menu} did not open.");
            foreach (string id in ids)
            {
                ReviewMenuEntry? entry = entries.FirstOrDefault(e => e.AutomationId == id);
                Check(entry is not null, $"{menu} has no entry {id}.");
                Check(entry!.Drawn, $"{menu} does not draw its entry \"{entry.Text}\".");
                Check(entry.IsEnabled, $"{menu} shows its entry \"{entry.Text}\" disabled.");
            }
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

        // The group's folders as the pane draws them: all of them, or none.
        void CheckFoldersDrawn(bool expected, string when)
        {
            (int shown, int total) = window.GroupFoldersShownForReview(groupId);
            Check(total == 2, $"{when}: the group holds {total} folders in the pane, not 2.");
            Check(shown == (expected ? total : 0),
                $"{when}: the pane draws {shown} of the group's {total} folders, not {(expected ? total : 0)}.");
            Check(window.IsGroupExpandedForReview(groupId) == expected,
                $"{when}: the group's item says {(expected ? "folded" : "unfolded")}.");
        }

        // The pane goes to the rail and comes back: NavigationView folds every
        // group as it reduces, which is not the person folding one.
        async Task RailAndBack()
        {
            window.SetPaneOpenForReview(false);
            await Task.Delay(300);
            window.SetPaneOpenForReview(true);
            await Task.Delay(300);
        }

        // Folded by a click on the group's row, through the item's own invoke,
        // so the window's handler records it. Still folded once the pane has
        // gone to the rail and come back, and after a sync of the pane.
        await Capture("groups-collapsed", async () =>
        {
            CheckFoldersDrawn(true, "Before the click");
            Check(window.ClickGroupForReview(groupId), "The group's item offers no invoke to click it.");
            await Task.Delay(300);
            CheckFoldersDrawn(false, "After a click on the group");
            await RailAndBack();
            CheckFoldersDrawn(false, "After the rail and back");
            await window.AddFolderForReviewAsync(documents);
            await Task.Delay(300);
            CheckFoldersDrawn(false, "After a sync of the pane");
        });

        // Unfolded by its chevron, through the item's expand pattern; still
        // unfolded once the pane has gone to the rail, which folds it on the
        // way, and come back. NavigationView raises its Collapsed for that
        // fold once the pane is closed, and its Expanding as it opens again
        // (seen 2026-11-25), so the two cancel out even without the guard of
        // OnGroupExpansionChanged: this state checks the outcome, not the
        // guard. A second click folds it and a third unfolds it: the click
        // works both ways.
        await Capture("groups-expanded-again", async () =>
        {
            Check(window.ExpandGroupForReview(groupId, true), "The group's item offers no expand pattern.");
            await Task.Delay(300);
            CheckFoldersDrawn(true, "After the chevron unfolded the group");
            await RailAndBack();
            CheckFoldersDrawn(true, "After the rail and back");
            Check(window.ClickGroupForReview(groupId), "The group's item offers no invoke to click it.");
            await Task.Delay(300);
            CheckFoldersDrawn(false, "After a second click on the group");
            Check(window.ClickGroupForReview(groupId), "The group's item offers no invoke to click it.");
            await Task.Delay(300);
            CheckFoldersDrawn(true, "After a third click on the group");
        });

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

        // The group's menu, opened by a click on its "...": its two entries,
        // drawn and enabled, inside the window.
        await Capture("group-menu", async () =>
        {
            IReadOnlyList<ReviewMenuEntry> entries = await window.OpenGroupMenuByClickForReviewAsync(groupId);
            CheckEntries(entries, "The group's menu", FoldersWindow.RenameGroupEntryId, FoldersWindow.RemoveGroupEntryId);
            Check(window.OpenPopupsInsideWindowForReview(), "The group's menu opened outside the window.");
        }, keepPopups: true);
        window.CloseMenusForReview();
        await Task.Delay(300);

        // The folder's menu, opened by a click on its "...", then its "Group"
        // entry opened as the pointer resting on it opens it.
        await Capture("folder-menu-groups", async () =>
        {
            IReadOnlyList<ReviewMenuEntry> entries = await window.OpenFolderMenuByClickForReviewAsync(documents);
            Check(entries.Count == 4 && entries.Any(e => e.AutomationId == FoldersWindow.GroupSubMenuId && e.IsSubMenu),
                $"The folder's menu holds [{string.Join(", ", entries.Select(e => e.Text))}].");
            CheckEntries(entries, "The folder's menu", FoldersWindow.GroupSubMenuId);
        }, keepPopups: true);

        string work = FoldersWindow.GroupChoiceId(groupId);
        await Capture("folder-group-submenu", async () =>
        {
            IReadOnlyList<ReviewMenuEntry> choices = await window.OpenSubMenuForReviewAsync(FoldersWindow.GroupSubMenuId);
            CheckEntries(choices, "The \"Group\" submenu",
                FoldersWindow.NoGroupChoiceId, work, FoldersWindow.NewGroupChoiceId);
            Check(choices.Count == 3, $"The \"Group\" submenu holds [{string.Join(", ", choices.Select(e => e.Text))}].");
            Check(choices.Single(e => e.AutomationId == work).IsChecked == true, "The \"Group\" submenu does not mark Work.");
            Check(choices.Single(e => e.AutomationId == FoldersWindow.NoGroupChoiceId).IsChecked == false,
                "The \"Group\" submenu marks \"No group\" for a folder in Work.");
            Check(window.OpenPopupsInsideWindowForReview(), "The \"Group\" submenu opened outside the window.");
        }, keepPopups: true);

        // "No group" chosen in the submenu: the folder leaves the group in the
        // store and in the pane. Then the same path puts it back into Work,
        // and the submenu marks it there.
        await Capture("folder-group-moved", async () =>
        {
            Check(window.ChooseMenuEntryForReview(FoldersWindow.NoGroupChoiceId), "\"No group\" could not be chosen.");
            await Task.Delay(300);
            Check(window.FolderGroupIdForReview(documents) is null, "\"No group\" left the Documents folder in a group.");
            CheckPaneOrder();
            Check(window.GroupFoldersShownForReview(groupId).Total == 1, "The group's item still holds the Documents folder.");

            await window.OpenFolderMenuByClickForReviewAsync(documents);
            IReadOnlyList<ReviewMenuEntry> choices = await window.OpenSubMenuForReviewAsync(FoldersWindow.GroupSubMenuId);
            Check(choices.Any(e => e.AutomationId == FoldersWindow.NoGroupChoiceId && e.IsChecked == true),
                "The \"Group\" submenu does not mark \"No group\" for a folder in no group.");
            Check(window.ChooseMenuEntryForReview(work), "Work could not be chosen.");
            await Task.Delay(300);
            Check(window.FolderGroupIdForReview(documents) == groupId, "Choosing Work did not put the Documents folder back in it.");
            CheckPaneOrder();
            Check(window.GroupFoldersShownForReview(groupId) == (2, 2), "The group does not draw its two folders again.");
        });
        window.CloseMenusForReview();
        await Task.Delay(300);

        // The group goes by its menu's Remove; its folders stay, in no group,
        // and the pane follows.
        await Capture("groups-removed", async () =>
        {
            await window.OpenGroupMenuByClickForReviewAsync(groupId);
            Check(window.ChooseMenuEntryForReview(FoldersWindow.RemoveGroupEntryId), "Remove group could not be chosen.");
            await Task.Delay(300);
            CheckPaneOrder();
            Check(window.GroupTooltipForReview(groupId) is null, "The removed group is still in the pane.");
            Check(window.FolderGroupIdForReview(documents) is null && window.FolderGroupIdForReview(projects) is null,
                "A folder of the removed group is still in a group.");
        });
        window.CloseMenusForReview();
        window.SetPaneOpenForReview(false);
    }
}
