# Turn a text or recordings into the text you want, one at a time or many at once

## Need

Draft, not confirmed

**Context of use.** The user, on their own computer, has a text to rework or a recording to turn into text, outside a dictation. Sometimes it is one text they want to shape and use; sometimes they only want transcripts, of many recordings at once.

**User need.** The user needs to turn a text or a recording into the text that suits them, trying ways of rewriting it as far as they like, and to turn many recordings into text files in one pass, in order to use the result without handling a file in between.

**Intended outcome.**

- A text or a recording can be brought in from one place that is always at hand.
- They try several ways of rewriting and stop at the one that suits them; every earlier version comes back.
- Their own corrections are kept when the text is rewritten.
- A long text, hours of speech, stays whole and readable.
- Many recordings become text files in one pass, each kept as soon as it is ready, and they know where the files go before and while it runs.
- They can read and correct any of those texts, and the correction is kept without a step of theirs.
- A failure never takes away what they had.
- The work goes on while they do something else.

## Journey

| The user wants to | The product must |
| --- | --- |
| bring a text or a recording in | take a pasted text, or transcribe a recording as it goes, with their usual transcription settings |
| shape the text | rewrite all of it with the profile they pick, from the text as it stands now, hand edits included |
| compare and go back | keep every version as a step they can undo and redo |
| take the result | copy it, or save it as a text file named after the recording |
| produce many transcripts | start at once on all the recordings chosen, save each text next to its recording as soon as it comes, and say so before and while it runs |
| check and correct one of them | show it whole, and keep every change on its file without asking |
| rewrite them all | rewrite each saved text with one profile and write it over its file |

When it goes another way:

- No provider is configured for rewriting: the user is told one is needed, and where to set it.
- A transcription or a rewrite fails: the text is left as it was before it started.
- In a batch, one file fails: only that file stops. A rewrite that does not come back keeps the transcript.
- The user stops a batch: the file in progress finishes and is saved, the rest is not transcribed.
- The user closes the window while it works: the work goes on, and the window comes back with its progress.

## Answer

Words a reader needs:

- A **profile** rewrites a text in its own way, lighter or heavier. The profiles are those of the Rewriting page.
- The **field** is the text field at the centre of the window, which holds one text.
- The **list** takes the field's place when several audio files are chosen: one line per file.
- A **batch** is the run that transcribes the files of the list in turn.

### The way in: the tray menu

1. The tray's left-click menu holds "Transcribe and rewrite…", which opens the window, in the actions group after Record. It is the one entry for a text and for audio files.
2. The left-click menu ends on Settings and Quit. With Developer mode on, the right-click menu holds Logs and Restart at its bottom.

### The window

1. Top row, left: "Transcribe audio files…". Top row, right: Undo, Redo, Copy, Save.
2. Under it, the field, which takes all the space the window leaves it. It can be edited at every moment except while a transcription or a rewrite writes into it.
3. Bottom row, left: the Rewrite button with the profile drop-down next to it, read as one phrase ("Rewrite · Mail pro"). Bottom row, right: a gear alone, with a tooltip, which opens Settings on the Rewriting page.
4. The window has no transcription setting of its own: the transcription follows the pipeline set in Settings (model, language, preprocessing).
5. The window opens small the first time, then at the size and place the user left it. Maximised, it keeps the same rows: the field grows, and the controls stay at their corners.

### One text in the field

1. Before anything: the field is empty and its placeholder says that a text can be pasted or a file transcribed. Copy, Save and Rewrite are disabled while the field is empty.
2. Before any choice, the tooltip of "Transcribe audio files…" says where the texts go: one file opens its text in the field; several are transcribed in a batch, each text saved next to its audio file.
3. Choosing files starts the transcription at once; there is no step between the choice and the work. One file, with no list open, goes into the field.
4. While a transcription or a rewrite runs: the field wears the HUD's animated outline, and Rewrite and "Transcribe audio files…" are disabled.
5. During a transcription, each utterance appears at the end of the field as soon as it is transcribed, whatever the dictation's streaming setting: a file is always transcribed utterance by utterance.
6. With a long text, three hours of a podcast for example, the field scrolls with its scroll bar; nothing is hidden behind "show more", and the display keeps up without stutter.
7. Rewrite rewrites the whole text of the field with the profile shown, starting from the field as it is now, hand edits included. A rewrite of a pasted text comes back whole; it is not shown progressively.
8. Changing the profile does nothing by itself; only Rewrite applies it.
9. Every replacement of the field's text, by a transcription, a rewrite or a paste, is one step of Undo and Redo. Example: transcribe, then rewrite with Mail pro, then with Formal: Undo twice brings back the transcript.
10. Copy puts the field's text on the clipboard.
11. Save, or Ctrl+S, opens "Save as" with a .txt name: the audio's name in its folder when the text came from a file, a neutral name otherwise.
12. With no provider configured for rewriting, Rewrite stays visible, and its click says that a provider is needed, with a way to the Rewriting page.
13. A failed transcription or rewrite leaves the field as it was before it started, with the usual failure message.

### Several files: the list

1. Several files chosen make the list, which takes the field's place, and the transcription starts at once. The top and bottom rows stay where they are.
2. Once a list exists, the top-left button reads "Add audio files…", and every choice joins the list, one file or several. A file already in the list that failed or was stopped goes back to waiting when it is chosen again; a file already saved or waiting is left as it is. During a batch, the new files are taken in turn by the batch already running.
3. Each file is saved as a .txt beside its audio, under the audio's name, as soon as its transcript comes, before anything else happens to it. A name already taken gets " (2)", as Explorer names a copy.
4. Above the list, one line says how far the batch is ("3 of 5 saved · 1 failed · 1 not transcribed"; a count of zero is not shown), and a caption under it says that each text is saved next to its audio file, with an "Open folder" link that shows the first file's text in its folder.
5. Each line of the list shows a status mark in a slot of its own (a ring while the file works, a check once saved, a warning when the rewrite did not come back, an error mark when it failed), the file's name on two lines at most, a caption with its state when it is not simply saved (Waiting, Transcribing…, Rewriting…, Rewritten, Couldn't rewrite, transcript kept, Not transcribed, or the failure) followed by its duration, and, once it holds a text, the opening of that text on two lines at most with a chevron on the right.
6. While a batch runs, a Stop button sits beside the top-left button. The file in progress finishes and is saved; the files still waiting are marked "Not transcribed" and nothing is written for them. Stop belongs to the list: it is not shown in the field.
7. A right-click on a line offers "Show in folder", which selects the file's .txt in Explorer.
8. On the list, Undo, Redo, Copy and Save are hidden: each file is already saved.
9. A file that fails stops only itself.

### One file of the list, opened in the field

1. A click on a line that holds a text opens the whole text in the field, which scrolls for a long text, as one step like any replacement.
2. Above the field, a breadcrumb reads "All files › the file's name", with the caption "Changes are saved automatically". Save is not shown. "All files" goes back to the list.
3. Every change to the text, a typed correction, a rewrite or an Undo, is written over the file's .txt a moment after it stops, and again when the field leaves the file: back to the list, to another file, or the window closed. A change that cannot be written is said so, and Copy keeps the text.
4. A text opened only to be read is never written back: its .txt stays untouched.
5. Undo, Redo and the field's history belong to the file opened: opening another file never brings back the text of the previous one.

### Rewriting every file

1. Rewrite and its profile list are hidden on the list until one file holds a text. The button then reads "Rewrite all", and its tooltip says it rewrites every saved text and writes it over its .txt.
2. It rewrites each saved file in turn with the profile shown. Stop ends it after the file in progress.
3. A rewrite that does not come back keeps the file's text, on disk and on screen, and its line says so.

### Closing the window

1. Closing the window while it works, or while a batch runs, hides it; the work goes on, and the tray entry brings it back with its progress.
2. A file open in the field is saved before the window hides or closes.

## Limits

- The window rewrites with the profiles of the Rewriting page; it creates no profile and edits none.
- Sending the result by mail is not covered here: it belongs to Deckle Mail, later.

## Open

1. **Dropping files on the window.** Audio files dropped on the window show a drop zone over the whole window. A single target seems enough: one file into the field, several into the list, as "Transcribe audio files…" does. To confirm with the user. Depends on it: whether the window has one drop target or several.
2. **The width of the text's lines when maximised.** Capped when the window is very wide, as Word does, or left to run the full width? To judge on a capture. Built today: not said.
3. **"Show in Deckle Folders" on the list's lines**, once Deckle Folders exists. Built today: "Show in folder" only.
