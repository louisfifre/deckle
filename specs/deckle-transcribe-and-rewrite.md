# Deckle — Transcribe and rewrite

Written on 2026-11-14 from the forms Louis settled that day over ASCII wireframes: one text field at the centre for a single text. Revised the same day (Louis): the step-by-step assistant for producing files is gone, its entry with it; several files chosen in the window turn its centre into the list of those files.

## Intention

Louis has a text to rework or an audio file to turn into text, outside a dictation. Today the only way in is "Transcribe audio file" in the tray, which writes a .txt next to the audio, and nothing rewrites a text that was not just typed. He wants one window, opened from the tray, where a text pasted or transcribed can be rewritten by any profile, stopping at whichever step suits him, without a text file in between. Separately, when he only wants files, he wants them produced in one pass, many at once.

## Journeys

**Rewrite a pasted text.** Louis opens the window from the tray, pastes a text into the field, keeps or picks a profile, clicks Rewrite. The field now holds the rewritten text. He tries another profile, clicks Rewrite again; Undo brings back the earlier version. He clicks Copy and closes the window.

**Transcribe an audio file, then rewrite it.** Louis clicks "Transcribe audio…", picks a recording. The field fills with the transcript as it comes, utterance by utterance, inside the animated outline the HUD shows while it works. He corrects a word by hand, clicks Rewrite, then Save, which offers the audio's name as a .txt in its folder.

**Produce files.** Louis wants transcripts of five recordings. In the same window he clicks "Transcribe audio…" and picks the five. The field gives way to the list of the five files, each saved as a .txt beside its audio as soon as its text comes. He reads the opening of a transcript on its line, clicks "Show more" to read it whole in the field, comes back with "All files", then clicks "Rewrite all" with a profile and opens the folder where the .txt files landed.

## Surfaces

### Tray menu

1. The left-click menu holds "Transcribe and rewrite…", which opens the window, in the actions group after Record. It is the one entry for a text and for audio files.
2. The developer menu (right click, Developer mode on) holds Logs and Restart at its bottom; the left-click menu ends on Settings and Quit.

### Transcribe and rewrite window

1. Top row, left: "Transcribe audio…". Top row, right: Undo, Redo, Copy, Save.
2. Under it, the text field, which takes all the space the window leaves it. It is a rich edit field (RichEditBox), editable at every moment except while a transcription or a rewrite writes into it.
3. Bottom row, left: the Rewrite button, then the profile drop-down next to it, read as one phrase ("Rewrite · Mail pro"). Bottom row, right: a gear icon alone, with a tooltip, which opens Settings on the Rewriting page.
4. Before anything: the field is empty and shows a placeholder saying a text can be pasted or a file transcribed. Copy, Save and Rewrite are disabled while the field is empty.
5. While a transcription or a rewrite runs: the field wears the HUD's animated outline, and Rewrite and "Transcribe audio…" are disabled.
6. During a transcription, each utterance appears at the end of the field as soon as it is transcribed, whatever the dictation's streaming setting: a file is always transcribed utterance by utterance.
7. With a long text, three hours of a podcast for example, the field scrolls with its scroll bar; nothing is hidden behind "show more", and the display keeps up without stutter.
8. No provider configured for rewriting: Rewrite stays visible and its click says that a provider is needed, with a way to the Rewriting page.
9. A failed transcription or rewrite leaves the field as it was before it started, with the usual failure message.
10. The window opens small the first time, then at the size and place Louis left it.
11. Maximised, the window keeps the same rows: the field grows, the controls stay at their corners.

### Several files

1. "Transcribe audio…" takes one file or several. One file goes into the field, as above. Several replace the field with the list of those files; the top and bottom rows stay where they are.
2. Each line: the file's name and duration, its state (waiting, transcribing, rewriting, saved, failed) and, once saved, the opening of its text on three lines at most, with "Show more" and "Open folder".
3. Each file is saved as a .txt beside its audio as soon as its transcript comes, before anything else happens to it.
4. "Show more" puts the file's whole text in the field, one step of Undo like any replacement; a long text scrolls there. An "All files" button, top left, goes back to the list.
5. On the list, Undo, Redo, Copy and Save are hidden: each file is already saved. The Rewrite button reads "Rewrite all": it rewrites every saved file with the profile shown and writes each rewrite over its .txt.
6. A file that fails stops only itself. A rewrite that does not come back keeps the file's text, on disk and on screen, and its line says so.
7. A new choice of several files replaces the list; the earlier files are already on disk. One file chosen while a list exists goes into the field, and "All files" stays.
8. Closing the window while it works hides it; the work goes on, and the tray entry brings it back with its progress.

## Behaviours

- Rewrite rewrites the whole text of the field with the profile shown, starting from the field as it is now, hand edits included.
- Every replacement of the field's text, by a transcription, a rewrite or a paste, is one step of Undo and Redo. Example: transcribe, then rewrite with Mail pro, then with Formal: Undo twice brings back the transcript.
- Changing the profile does nothing by itself; only Rewrite applies it.
- The transcription follows the pipeline set in Settings (model, language, preprocessing); the window has no transcription setting of its own.
- Save opens "Save as" with a .txt name: the audio's name in its folder when the text came from a file, a neutral name otherwise. Ctrl+S does the same.
- On the list, each .txt is saved next to its audio file under the audio's name; a name already taken gets " (2)", as Explorer names a copy.

## Limits

- The window rewrites with the profiles of the Rewriting page; it creates no profile and edits none.
- A rewrite of a pasted text comes back whole; it is not shown progressively.
- Sending the result by mail belongs to Deckle Mail, later.

## Open

- **Dropping files on the window.** Dropping audio files shows a drop zone over the whole window, a faint accent veil with a dashed outline that grows a little. With the assistant gone, a single target seems enough: one file into the field, several into the list, as "Transcribe audio…" does. To confirm with Louis.
- **Line width when maximised.** Cap the width of the text's lines when the window is very wide, as Word does, or let them run the full width? To judge on a capture.
- **"Show in Deckle Folders"** on the list's lines, once Deckle Folders exists.
