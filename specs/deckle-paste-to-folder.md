# Save what you copied as a file in a folder, with one paste

## Need

Draft, not confirmed

**Context of use.** The user, on their own computer, has just copied something: a passage of text, a screenshot or an image, or files in Explorer. They want it kept as a file in a folder of theirs, now, while they go on with what they were doing.

**User need.** The user needs to turn what is on the clipboard into a file in a folder they chose, with one gesture, in order to keep it without opening an editor, naming a file or going through a save dialog.

**Intended outcome.**

- What they copied becomes a file in their folder with one gesture.
- They see at once whether it worked, and why when it did not.
- They know where it goes before they paste.
- Nothing they already had is overwritten.
- The tool takes almost no room and stays out of the way of their work.
- What they pasted does not leave the machine.

## Journey

| The user wants to | The product must |
| --- | --- |
| keep something they copied | take a Ctrl+V on its window and write the clipboard's content into the chosen folder at once |
| know where it goes | show the folder before any paste, and let them choose another |
| know it worked | say Done once the file is written, or Failed with the reason |
| go on working | stay small and above their windows, and move wherever they drag it |

When it goes another way:

- No folder chosen yet: the paste fails, says to choose a folder first, and Choose folder is the next step.
- The chosen folder no longer exists: the paste fails and says so.
- The clipboard holds nothing that can be saved: the paste fails and says so.
- A name is already taken in the folder: the new file gets the next free number, and the old one is left as it is.
- A copied file has vanished before the paste: the paste fails; the files already copied stay.

## Answer

Words a reader needs:

- The **paste zone** is the area at the centre of the window, which looks like a text field and receives a paste. It is not an editor: nothing is typed or kept in it.
- The **status line** is the line under the paste zone: an icon and one word.
- The **folder line** is the line at the bottom of the window: the chosen folder and the Choose folder button.

### The way in: the tray menu

1. The tray's left-click menu holds "Paste to folder", which opens the window, in the actions group.
2. A second click while the window is open brings the same window to the front; there is only one.

### The window

1. The window is small, has no title bar and no header. Its only chrome is a close button, a small X in its top-right corner.
2. From top to bottom: the paste zone, with the placeholder "Paste here"; the status line; the folder line.
3. The window stays above the other windows, does not resize, and has no taskbar button.
4. Pressing anywhere in the window and dragging moves it, the paste zone included. Only the close button and Choose folder do not move it.
5. A click anywhere in the window, without dragging, gives it the focus, so the next Ctrl+V pastes into it.
6. Ctrl+V pastes whatever part of the window has the focus.

### Before the first paste

1. The status line is empty.
2. With no folder chosen, the folder line says no folder is chosen, and Choose folder is the obvious step.
3. With a folder chosen, in this run or an earlier one, the folder line shows its path, shortened when it is long, and its whole path in a tooltip.

### A paste

1. Ctrl+V writes what the clipboard holds into the chosen folder at once; there is no step between the paste and the file.
2. Files copied in Explorer are copied into the folder, each under its own name. A copied folder is copied whole, under its own name.
3. Otherwise, an image is saved as a PNG file.
4. Otherwise, a text is saved as a UTF-8 text file (.txt).
5. A saved image or text is named for the moment of the paste, in local time: "paste-2026-11-18-142305.png" for 14:23:05 on 18 November 2026.
6. A name already taken in the folder gets " (2)", then " (3)", before its extension, as Explorer names a copy. A file already there is never overwritten.
7. Once written, the status line shows a check mark and "Done".
8. When the paste fails, the status line shows an error mark and "Failed", with the reason in a tooltip or a short text beside it: choose a folder first; the folder no longer exists; the clipboard holds nothing to save; a copied file no longer exists; the file could not be written.
9. A failed copy of several files leaves in the folder the files copied before it stopped.
10. The window shows no history: each paste replaces the status line of the one before.

### Choosing the folder

1. Choose folder opens the Windows folder picker, the way Deckle's other folder choices do.
2. The folder chosen is kept at once, and the window opens with it the next time, after Deckle restarts too.
3. Cancelling the picker keeps the folder as it was.

### Closing

1. The close button closes the window. The folder stays chosen for the next time.

### Across moments

1. What the user pastes stays on the machine. The log says how each paste ended, its kind and size, never its text, its image, a file name or the folder's path.

## Limits

- The window saves; it does not show, rename or delete what was saved.
- One folder at a time: there is no list of recent folders.
- Dropping files or images onto the window is not covered here.

## Open

1. **Formats beyond text and PNG.** A rich-text or HTML copy is saved as its plain text today. Should it keep its formatting (.rtf, .html), or stay plain? Built today: plain text. Depends on it: which clipboard formats the window reads.
2. **The window's place when it opens again.** The place the user left it, or near the tray each time? Built today: decided by the window's builder, see its report. Depends on it: whether the place is kept with the folder.
