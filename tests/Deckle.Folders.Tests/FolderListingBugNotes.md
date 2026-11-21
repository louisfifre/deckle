# Folder listing bug notes

## A folder Windows refused to list read as an empty folder

- **Trigger:** `FolderListing.List` on a folder whose "list folder contents" right is denied to the user, as a folder of another account or a system folder denies it. Reproduced in a test by a deny rule on a scratch folder (`DenyListing`).
- **Observed symptom:** The listing came back succeeded with no entry, so the file tool would say "Ce dossier est vide." instead of "Vous n'avez pas accès à ce dossier." Observed in `AFolderWindowsRefusesToListSaysSoRatherThanEmpty` (`Failure` was `None`, not `AccessDenied`) on 2026-11-15; not observed in the app, whose self-check walks no refused folder.
- **Cause:** The listing's `EnumerationOptions` set `IgnoreInaccessible = true`. With no recursion, the only directory that option can skip is the root itself: .NET then opens no handle and ends the enumeration silently, without throwing. The failing test passes once the option is off. Inferred from the runtime's behaviour, not read in its source here: that the skip happens when the root's handle is opened.
- **Violated invariant:** A listing that could not read its folder says why; only a folder that was read and holds nothing is empty. The trap is commented at the options in `src/Deckle.Folders/Browsing/FolderListing.cs`.
- **Recurrence cue:** An unreadable or protected folder shown as empty; `IgnoreInaccessible = true` on an enumeration of one level; `FolderListingFailed` (event 6) missing from the log while a folder shows nothing.
- **Regression coverage:** `FolderListingTests.AFolderWindowsRefusesToListSaysSoRatherThanEmpty` (category `regression`) fails with the option on and passes with it off. `ARefusedSubFolderStillListsInItsParent` checks the fix did not turn a refused child into a failure of its parent.
