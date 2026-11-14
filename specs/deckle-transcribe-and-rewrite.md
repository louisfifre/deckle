# Deckle — Transcribe and rewrite

Written on 2026-11-14 from the forms Louis settled that day over ASCII wireframes: one text field at the centre for a single text, a step-by-step assistant for producing files.

## Intention

Louis has a text to rework or an audio file to turn into text, outside a dictation. Today the only way in is "Transcribe audio file" in the tray, which writes a .txt next to the audio, and nothing rewrites a text that was not just typed. He wants one window, opened from the tray, where a text pasted or transcribed can be rewritten by any profile, stopping at whichever step suits him, without a text file in between. Separately, when he only wants files, he wants them produced in one pass, many at once.

## Journeys

**Rewrite a pasted text.** Louis opens the window from the tray, pastes a text into the field, keeps or picks a profile, clicks Rewrite. The field now holds the rewritten text. He tries another profile, clicks Rewrite again; Undo brings back the earlier version. He clicks Copy and closes the window.

**Transcribe an audio file, then rewrite it.** Louis clicks "Transcribe a file…", picks a recording. The field fills with the transcript as it comes, utterance by utterance, inside the animated outline the HUD shows while it works. He corrects a word by hand, clicks Rewrite, then Save, which offers the audio's name as a .txt in its folder.

**Produce files.** Louis wants transcripts of five recordings without reading them. The assistant takes him through numbered steps: Files, Rewrite, Result. He adds the files, decides whether they are rewritten and by which profile, then follows each file's progress and opens the folder where the .txt files landed.

## Surfaces

### Tray menu

1. The left-click menu holds "Transcribe and rewrite…", which opens the window, in the actions group after Record. "Transcribe audio files…" stays under it, writing each .txt beside its audio, until the file assistant exists.
2. The developer menu (right click, Developer mode on) holds Logs and Restart at its bottom; the left-click menu ends on Settings and Quit.

### Transcribe and rewrite window

1. Top row, left: "Transcribe a file…". Top row, right: Undo, Redo, Copy, Save.
2. Under it, the text field, which takes all the space the window leaves it. It is a rich edit field (RichEditBox), editable at every moment except while a transcription or a rewrite writes into it.
3. Bottom row, left: the Rewrite button, then the profile drop-down next to it, read as one phrase ("Rewrite · Mail pro"). Bottom row, right: a gear icon alone, with a tooltip, which opens Settings on the Rewriting page.
4. Before anything: the field is empty and shows a placeholder saying a text can be pasted or a file transcribed. Copy, Save and Rewrite are disabled while the field is empty.
5. While a transcription or a rewrite runs: the field wears the HUD's animated outline, and Rewrite and "Transcribe a file…" are disabled.
6. During a transcription, each utterance appears at the end of the field as soon as it is transcribed, whatever the dictation's streaming setting: a file is always transcribed utterance by utterance.
7. With a long text, three hours of a podcast for example, the field scrolls with its scroll bar; nothing is hidden behind "show more", and the display keeps up without stutter.
8. No provider configured for rewriting: Rewrite stays visible and its click says that a provider is needed, with a way to the Rewriting page.
9. A failed transcription or rewrite leaves the field as it was before it started, with the usual failure message.
10. The window opens small the first time, then at the size and place Louis left it.
11. Maximised, the window keeps the same rows: the field grows, the controls stay at their corners.

### File assistant

1. One screen per step, the numbered steps listed on the side, Back and Next at the bottom, as in a Windows setup.
2. ① Files: the list of files, each with its duration and a remove button, and "Add files…".
3. ② Rewrite: a "Rewrite" switch and, when it is on, the profile.
4. ③ Result: each file's state (waiting, transcribing, rewriting, done, failed) and where its .txt is saved, with "Open folder".
5. The assistant takes one file as well as many: it is the way to produce files, not a batch mode.

## Behaviours

- Rewrite rewrites the whole text of the field with the profile shown, starting from the field as it is now, hand edits included.
- Every replacement of the field's text, by a transcription, a rewrite or a paste, is one step of Undo and Redo. Example: transcribe, then rewrite with Mail pro, then with Formal: Undo twice brings back the transcript.
- Changing the profile does nothing by itself; only Rewrite applies it.
- The transcription follows the pipeline set in Settings (model, language, preprocessing); the window has no transcription setting of its own.
- Save opens "Save as" with a .txt name: the audio's name in its folder when the text came from a file, a neutral name otherwise. Ctrl+S does the same.
- In the assistant, each .txt is saved next to its audio file, as "Transcribe audio file" does today.

## Limits

- The window rewrites with the profiles of the Rewriting page; it creates no profile and edits none.
- A rewrite of a pasted text comes back whole; it is not shown progressively.
- Sending the result by mail belongs to Deckle Mail, later.

## Open

- **Dropping files on the window.** Dropping audio files shows a drop zone over the whole window, a faint accent veil with a dashed outline that grows a little. Does it offer two halves, "Transcribe here" (into the field) and "Transcribe to file" (the assistant), or a single target? Depends on it: whether the assistant can be reached by drop.
- **Line width when maximised.** Cap the width of the text's lines when the window is very wide, as Word does, or let them run the full width? To judge on a capture.
- **"Show in Deckle Folders"** in the assistant's Result step, once Deckle Folders exists.
- **The tray entries once the assistant exists**: does "Transcribe audio files…" open the assistant, or does the window offer the assistant itself, leaving one entry?
