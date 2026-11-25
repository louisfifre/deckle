# Speak as long as you want, over everything, and get back a well-framed text

## Need

**Context of use.** A person thinks aloud, sometimes for more than an hour. They talk about what passes before their eyes, one subject then another, often while doing something else. They are at their computer, or elsewhere with their phone. What they say belongs to no application.

**User need.** The user needs to start the listening and stop attending to it, in order to get back everything they said in a well-framed text that they put wherever they want.

**Intended outcome.**

1. One gesture starts the listening from anywhere, and the same one stops it.
2. While they speak, nothing asks for their attention and nothing rushes them.
3. They are never cut off: a hesitation or a pause in the middle of a sentence closes nothing.
4. Only their voice counts: a rub or a noise around them does not pass for speech.
5. An hour holds up as well as two minutes, and nothing of what they said is lost.
6. They change subject, stop, resume; the text keeps these separations.
7. To find their thread again, their last words read at a glance, without hiding what they are looking at.
8. The final text says what they said, with their words.
9. The experience is the same on the computer and on the phone.

## Journey

| The user wants to | The product must |
| --- | --- |
| start speaking from wherever they are, without leaving what they do | start the dictation on one gesture, the same in every application, and leave them where they were |
| think aloud for as long as it takes, often doing something else | keep listening without asking for anything, without cutting them on a pause or a hesitation, and take only their voice |
| find their thread again | show their last words at a glance, out of the way of what they are looking at |
| change subject, pause, resume | keep these separations in the text |
| stop and use what they said | stop on the same gesture, and have the whole text ready, framed and in their words, for them to put where they want |
| decide once how their dictations are rewritten | keep, for each length, which profile rewrites, and whether it rewrites while they speak or once they stop |
| speak at length and keep going | rewrite as they go when they chose it, without moving what they are reading, and hand the work to a heavier profile as the dictation grows |

When it goes another way:

- No voice reaches the microphone: the user is told once, early, and the dictation goes on.
- Nothing was said: nothing is delivered, and the clipboard keeps what it held.
- Deckle or the computer stops during a dictation: what was said comes back at the next start.
- Part of what was said cannot be transcribed: the user is told, and the rest of the text is delivered.
- The dictation stays shorter than the first step: the text is given back as said.
- The user chose to rewrite once they stop: the panel shows the text as said, and at Stop the rewritten text replaces it.
- Transcription is not streaming: every step rewrites at Stop.
- A rewrite fails: the user keeps the text they had, on screen and on the clipboard.

## Answer

Words a reader needs:

- A **dictation** is what the user says between the gesture that starts it and the one that stops it, and the text it gives back.
- The **dictation shortcut** is the shortcut "Start or stop dictation", Win + the key left of 1 unless the user changed it in Settings › Shortcuts.
- The **HUD** is the small surface that shows the state of the dictation, over every window.
- The **transcript panel** is the panel that shows the dictation while the user speaks.
- An **utterance** is a stretch of speech the dictation transcribes in one go, closed by a silence or by Stop.
- The **text as said** is the transcript before any rewrite.
- **Stop** is the end of the dictation, when the user stops recording.
- A **profile** rewrites a text in its own way, lighter or heavier. The profiles are those of the Rewriting page. Example: Affinage, Lissage, Arrangement.
- A **step** is a length from which one profile rewrites the dictation.
- A **block** is the stretch of text a profile rewrites in one go.
- A step **takes over** when the dictation reaches its length: it rewrites everything already said, in its own blocks, then goes on alone.

The examples of rewriting use these settings: a first step Affinage from 1,000 characters in blocks of 600, a step Lissage from 4,000 in blocks of 2,000, a step Arrangement from 12,000 in blocks of 6,000.

### Before the first dictation

1. With no speech model on the machine, the dictation shortcut starts nothing, and the HUD says the model is missing and where to get it.
2. With no microphone that answers, the dictation shortcut starts nothing, and the HUD says so.

### Starting and stopping

1. From any application, the dictation shortcut starts a dictation, and the same shortcut stops it. Record, in the tray, does the same.
2. Starting a dictation takes nothing from the application in front: its window keeps the focus, and the user goes on with what they were doing.
3. The first words said after the gesture are in the text: the user does not wait for a signal to speak.
4. A dictation has no length limit. "Maximum recording length", on the Recording page, sets one only when the user chooses it.
5. The stop is instant: the recording ends at the gesture that stops it, and nothing said after the gesture is kept.

### While the user speaks

1. The HUD shows that the dictation is listening, and nothing on it presses the user or asks for a gesture. What it shows is question 1 under Open.
2. Nothing counts down while the user is silent: they never have to make a sound to keep the dictation going.
3. A pause, however long, ends nothing for the user: the dictation goes on until they stop it.
4. A hesitation, a stammer or a pause in the middle of a sentence never splits the sentence in the text. Where an utterance closes is question 4 under Open.
5. Only the user's voice adds text: a click, a scratch, the rub of a garment or music around them adds none.
6. When the user changes subject, stops and resumes, the text keeps these separations. How the change is marked, and what it leaves in the text, is question 2 under Open.
7. A dictation of an hour goes as one of two minutes: the same gestures, nothing slows down, and the text is whole.

### While the user speaks: the transcript panel

1. The panel shows the text of the dictation as it will be delivered: what it shows is what the clipboard gets.
2. The last words said read at a glance: they are the ones the user looks for to find their thread, and the older text gives way to them. How they are shown is question 3 under Open.
3. The panel never hides what the user is looking at: as the cursor comes near, it fades until what lies under it can be read and used.
4. With "Rewrite while I speak" off, or before the first step is reached, the panel shows the text as said.
5. When the first step is reached with "Rewrite while I speak" on, the beginning is rewritten block by block. Each block changes in place, and the rest of the text does not move. The last block, not closed yet, stays as said.
6. When a higher step takes over, each block keeps its earlier text until its new version arrives, then changes in place. No block goes back to the text as said.
7. A block whose rewrite fails keeps what it showed.

### At Stop

1. Nothing is pasted: the text is on the clipboard, the HUD says it was copied, and the user puts it where they want.
2. The last words said before the gesture are in the text.
3. The transcript panel shows the delivered text beside the HUD's message, then both leave.
4. With "Rewrite while I speak" on and a step reached: the step rewriting at Stop rewrites the block it had not finished, and the rewritten text is on the clipboard as soon as that block comes back. The user waits for that block only.
5. With it off and a step reached: the text as said is on the clipboard at once, and the whole text rewritten by the profile of the highest step reached replaces it when that profile is done. The HUD says so.
6. With no step reached: the text as said is on the clipboard.
7. With nothing said: nothing is copied, the clipboard keeps what it held, and the HUD leaves.

### When it fails

1. When no voice reaches the microphone in the first seconds, the HUD says so once, and the dictation goes on.
2. When part of what was said cannot be transcribed, the rest of the text is delivered and the user is told that a part is missing: they are never left to find a hole.
3. When Deckle or the computer stops during a dictation, what was said comes back at the next start, on the clipboard, and the HUD says it is a recovered dictation. When it comes back only in part, the HUD says that too. What comes back is question 7 under Open.
4. When a rewrite fails, what was on the clipboard stays there, with the usual failure message.
5. When the clipboard cannot be written, the HUD says so, and the text is not lost.

### Setting the rewriting: the Rewriting page, section Dictation

1. With "Rewrite dictations" off, a dictation arrives as said, and nothing else of the section is shown.
2. With it on, the section shows "Rewrite while I speak", the timeline, one card per step, then "Add a step". The first step always exists and cannot be removed.
3. "Rewrite while I speak" appears only when transcription is streaming. Without streaming transcription, every step rewrites at Stop.
4. Every step, the first included, reads "From" and a length. A dictation shorter than the first step's length stays as said, and a first step from 0 rewrites every dictation from its beginning. Example: from 1,000, a dictation of 600 characters is not rewritten.
5. Every later step carries a length and a profile. A step added lands after the last one. One switch, "Rewrite while I speak", decides for all steps when they rewrite: no step has a switch of its own for it.
6. With "Rewrite while I speak" on, every card also shows "in blocks of" with a number of characters: the size of the stretch its profile rewrites in one go. Off, the block size is hidden and kept.
7. Switching "Rewrite while I speak" changes no length. Example: Arrangement from 12,000 stays from 12,000 in both modes.
8. A block size can be set up to at least 6,000 characters. Example: Arrangement in blocks of 6,000 is accepted as typed.
9. The cards carry no description.
10. The timeline shows one row per step, with the step's name in its row, and the same rows in both modes. Under the rows, a scale gives lengths with an approximate speaking time written with "≈". Example: 12,000 ≈ 17 min.
11. With "Rewrite while I speak" off, each row shows the range of lengths its step takes: from its length to the next step's.
12. With it on, each row shows its step's blocks, from the beginning of the dictation to where the next step takes over. The blocks before the step's own length are pale: they are the text it rewrites at once when it takes over. One caption under the timeline says so, in this mode only.
13. The timeline shows and never writes: the values are changed in the cards.

### Rules

- The text says what the user said, with their words. A rewrite keeps every notion they said: a notion said twice may be brought together in one place, never dropped.
- Nothing of what the user said is lost between the gesture that starts the dictation and the text delivered.
- What the HUD and the tray show is the real state of the recording: they say "listening" only while the microphone is open, and leave it at the gesture that stops it.
- A step's length is counted in characters of the text as said.
- A rewrite always starts from the text as said, never from an earlier rewrite.
- A dictation is always rewritten from its beginning, never from the length of the step. Example: 1,500 characters, first step at 1,000: all 1,500 characters are rewritten by Affinage.
- While the user speaks, one step rewrites at a time: the highest one reached. When a dictation reaches a step's length, that step rewrites everything already said, in its own blocks, then goes on alone, and the step before stops. Example: at 4,000 characters, Lissage rewrites the first 4,000 at once in two blocks, and Affinage sends nothing more.
- A block closes at the first pause once it reaches its step's block size.
- When a step takes over, it cuts the text already said along the blocks of the step before: each of its blocks joins whole earlier blocks until it reaches its size, and the last one may stay shorter. Example: at 4,000 characters, Lissage sends one block of about 2,500 and one of about 1,500. A step with smaller blocks than the one before resends that step's blocks as they are.

## Limits

- The phone: Deckle runs on Windows only, so line 9 of the need is not met by this product. Dictating on the phone is carried by the management project "Dictée Android".
- Speaking into a field or a tab of another application, the text going where the user clicked, is another goal, with no spec yet.
- An assistant that listens and answers by voice, reads its answers aloud with play and pause, and may stop the user when asked to, is another goal, with no spec yet.
- Turning a recording file or a pasted text into text is the goal of `deckle-transcribe-and-rewrite.md`.
- A block is rewritten without the blocks around it: a profile reorganizes inside a block, never across two. Rewriting at Stop is the only way to have a profile see the whole dictation.
- Taking over costs: each step that takes over sends again everything already said. With the example settings, a dictation of 15,000 characters sends about 31,000 characters in 15 requests while the user speaks, against 15,000 in one request at Stop.
- The speaking times on the timeline are orders of magnitude (700 characters ≈ 1 min), not measures.

## Open

1. **What the HUD shows while the user speaks.** The large running clock stresses the user. Options: an outline alone, which follows the voice; a small, quiet elapsed time; nothing but the transcript panel's last words. Built today: a clock in minutes, seconds and hundredths, with an outline whose brightness follows the voice and which counts down the pause left before an utterance closes. Depends on it: the HUD while recording.
2. **How a change of subject is marked, and what it leaves in the text.** The user wants not to touch anything, and also asked for a button to pass to another subject. Options for the mark: a gesture (a button or a shortcut), a spoken cue, a change detected from what is said, a long pause. Options for the trace: a new paragraph, a separating line, a heading per subject. Built today: no mark; every utterance closed on a pause starts a new paragraph. Depends on it: the frame of the delivered text, and question 4.
3. **How the last words are shown.** The user finds their thread in the most recent words, finds the panel holds a little too much text, and liked something laid over everything. Options: the recent words larger than the rest, in the panel beside the HUD; a layer over everything, whose text fades away entirely to show what is under it; fewer lines. Built today: the panel beside the HUD, on the side chosen in Settings › Transcription, with the whole text of the dictation, fading under the cursor. Depends on it: the transcript panel, and how a rewritten block shows in it.
4. **What "never cut" means for the moment an utterance closes.** Options: an utterance closes only on a pause after a complete sentence; a pause never shows in the text, only a change of subject does; the wait before closing adapts to the person speaking. Built today: an utterance closes after one to five seconds of silence, shorter as it grows, and each one closed starts a new paragraph. Depends on it: where paragraphs fall, when a rewrite block closes, and question 2.
5. **Which rewriting runs by default.** The user said a plain rewrite all the time, with Arrangement only when they say the same notion in two places, and fears losing notions on very long dictations. Options: one plain profile for every dictation, with Arrangement only on repetition; the steps by length of the Rewriting page; a gesture during the dictation to rewrite now. Built today: nothing rewritten until "Rewrite dictations" is on, then the steps by length. Depends on it: the section Dictation of the Rewriting page, and whether steps stay.
6. **Whether the rewrite may leave the machine.** The user rewrites with a hosted model today and does not trust local rewriting; the spec of the whole says what the user keeps of their own leaves the machine only where a goal says so, and then never with what identifies them. Options: hosted allowed for dictations, said where the provider is chosen; local only; hosted allowed once what identifies the user is taken out. Built today: the provider is chosen on the Rewriting page, local or hosted, and a hosted provider receives the transcript text. Depends on it: what this spec says leaves the machine.
7. **What comes back after a crash, and where a delivered text can be found again.** Options after a crash: the whole dictation, rewritten as chosen; the text as said only; the dictation picked up where it stopped. Options once delivered: the clipboard only; the last dictations kept somewhere to find them again. Built today: the text as said comes back on the clipboard at the next start, from the audio kept during the dictation or, failing it, from the text saved at each utterance; a delivered text lives on the clipboard only, and copying something else before pasting replaces it. Depends on it: line 5 of the need, and the place a dictation is kept.
8. **A block already read changes a second time while the user speaks.** The user reads the panel to find their place, reads the last segment, and sometimes scrolls up; whether a second change disturbs that is not known yet. Built today: a short cross-fade when animations are on. Other way, wanted by the user: a line-by-line rewrite animation in the HUD's colours. Depends on it: how the panel shows a block that takes over.
9. **The block left open at Stop.** Built today: the step rewriting at Stop rewrites it, and the user waits for that block, up to its block size. Against it: up to 6,000 characters to wait for with the example settings. To confirm in use. Depends on it: the wait at Stop.
10. **A block size suggested by the profile.** A profile could carry a block size that suits it (a light one 600, a heavy one 6,000), taken as the step's starting value when the profile is chosen. Built today: no suggestion; the block size stays on the step either way.
11. **A step's length: characters or speaking time?** Built today: characters, with the speaking time shown on the timeline only, since what makes a profile useful is the amount of text, and a silence must not make a step take over. Other way: speaking time.
12. **The last block cut when a step takes over.** Built today: sent as it is, shorter than the step's block size, since it was already closed. Against it: a short block gives a heavy profile little to reorganize. Other way: reopen it and let it grow with the speech, which the panel would then show as said again until it closes. To confirm in use.
