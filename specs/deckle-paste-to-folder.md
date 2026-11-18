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
| know it worked | list the saved file at the top of the window once it is written, or say it was not saved and why |
| go on working | stay small and above their windows, and move wherever they drag it |

When it goes another way:

- No folder chosen yet: the paste fails, says to choose a folder first, and Choose folder is the next step.
- The chosen folder no longer exists: the paste fails and says so.
- The clipboard holds nothing that can be saved: the paste fails and says so.
- A name is already taken in the folder: the new file gets the next free number, and the old one is left as it is.
- A copied file has vanished before the paste: the paste fails; the files already copied stay.

## Answer

Words a reader needs:

- The **inset** is the area that fills the window above the folder line. It looks like a text field and receives a paste, but it is not an editor: nothing is typed in it. It holds the placeholder before the first paste, then the list.
- The **list** is what was pasted since the window opened, shown in the inset, one row per saved file or per paste that failed, newest at the top.
- The **folder line** is the line at the bottom of the window: the chosen folder and the Choose folder button.

### The way in: the tray menu

1. The tray's left-click menu holds "Paste to folder…", which opens the window, in the actions group, after the switches and before Record.
2. A second click while the window is open brings the same window to the front; there is only one.

### The window

1. The window is small, has no title bar, no header and no status line. Its only chrome is a small close cross inside the inset, in its top-right corner; it stays there while the list scrolls, and no row runs under it.
2. From top to bottom: the inset, then the folder line.
3. The window stays above the other windows, does not resize, and has no taskbar button.
4. Pressing anywhere in the window and dragging moves it, the inset and its rows included. Only the close cross, Choose folder and the list's scrollbar do not move it.
5. A click anywhere in the window, without dragging, gives it the focus, so the next Ctrl+V pastes into it.
6. Ctrl+V pastes whatever part of the window has the focus.

### Before the first paste

1. The inset shows only the placeholder "Paste here".
2. With no folder chosen, the folder line says no folder is chosen, and Choose folder is the obvious step.
3. With a folder chosen, in this run or an earlier one, the folder line shows its path, shortened when it is long, and its whole path in a tooltip.

### A paste

1. Ctrl+V writes what the clipboard holds into the chosen folder at once; there is no step between the paste and the file.
2. Files copied in Explorer are copied into the folder, each under its own name. A copied folder is copied whole, under its own name.
3. Otherwise, an image is saved as a PNG file.
4. Otherwise, a text is saved as a UTF-8 text file (.txt).
5. A saved image or text is named for the moment of the paste, in local time: "paste-2026-11-18-142305.png" for 14:23:05 on 18 November 2026.
6. A name already taken in the folder gets " (2)", then " (3)", before its extension, as Explorer names a copy. A file already there is never overwritten.
7. While a save runs, a small progress ring and "Saving…" show at the top of the list.
8. Once written, each paste adds its rows at the top of the list, newest first:
   - an image: a small thumbnail of the image itself, then the saved file's name;
   - a text: a text document icon, the saved file's name, and under it the beginning of the text on one line, cut with an ellipsis;
   - copied files: one row per saved file or folder, with an icon for its kind (image, video, audio, document, folder, other file) and its name.
9. When the paste fails, one row says "Not saved" with an error mark, and the reason under it: choose a folder first; the folder no longer exists; the clipboard holds nothing to save; a copied file no longer exists; the file could not be written; the clipboard could not be read.
10. A failed copy of several files leaves in the folder the files copied before it stopped, and lists them under the row that says why.
11. A name too long for its row is cut with an ellipsis and shows whole in a tooltip. The list scrolls when it is full, with the wheel or the scrollbar, never sideways.
12. Rows are read, not pressed: a row has no action, no selection and no hover.
13. With a screen reader, each paste is announced as it ends: the saved file's name, the number of items saved, or that it was not saved and why.

### Choosing the folder

1. Choose folder opens the Windows folder picker, the way Deckle's other folder choices do.
2. The folder chosen is kept at once, and the window opens with it the next time, after Deckle restarts too.
3. Cancelling the picker keeps the folder as it was.
4. When the picker cannot open, the list says "Folder not changed" and why. When the choice cannot be kept for the next time, it says "Folder not remembered": the folder is used until Deckle quits.

### Closing

1. The close cross, or Escape, closes the window. The folder stays chosen for the next time.
2. Closing empties the list: the window opens again on the empty inset.

### Across moments

1. What the user pastes stays on the machine. The log says how each paste ended, its kind and size, never its text, its image, a file name or the folder's path.
2. The list lives in memory only: no name, excerpt or thumbnail of it is written to disk or to the log.

## Limits

- The window saves and lists what it saved; it does not open, rename or delete it.
- One folder at a time: there is no list of recent folders.
- Dropping files or images onto the window is not covered here.

## Open

1. **Formats beyond text and PNG.** A rich-text or HTML copy is saved as its plain text today. Should it keep its formatting (.rtf, .html), or stay plain? Built today: plain text. Depends on it: which clipboard formats the window reads.
2. **The window's place when it opens again.** The place the user left it, or near the tray each time? Built today: decided by the window's builder, see its report. Depends on it: whether the place is kept with the folder.
