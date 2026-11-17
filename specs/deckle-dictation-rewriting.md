# Dictate at any length and get back a text that reads as written, in your own words

## Need

Draft, not confirmed

**Context of use.** The user dictates on their own computer, in whatever they are working on, often at length: a few sentences, or twenty minutes of thinking aloud. They want text they can use elsewhere at once.

**User need.** The user needs what they dictated to come back as a text that reads as written while keeping their words, in order to use it without reworking it by hand.

**Intended outcome.**

- The text they get back reads as written, and says what they said, with their words.
- A short dictation gets a light touch; a long one gets more of the work done.
- They decide once how their dictations are reworked, by length and by moment, then dictate without thinking about it.
- While they speak, they can follow what they said and keep their place in it.
- Soon after they stop, the text is ready, and they decide where it goes: nothing is inserted for them.
- A rework that fails never takes away the text they already had.

## Journey

| The user wants to | The product must |
| --- | --- |
| decide once how their dictations are reworked | keep, for each length, which profile rewrites, and whether it rewrites while they speak or once they stop |
| dictate a few sentences | give back the text as said, or rewritten lightly once it is long enough |
| speak at length and keep going | rewrite as they go when they chose it, without moving what they are reading, and hand the work to a heavier profile as the dictation grows |
| stop and use the text | have the text ready at once, rewritten as far as the chosen setting goes, for them to put where they want |

When it goes another way:

- The dictation stays shorter than the first step: the text is given back as said.
- The user chose to rewrite once they stop: the panel shows the text as said, and at Stop the rewritten text replaces it.
- Transcription is not streaming: every step rewrites at Stop.
- A rewrite fails: the user keeps the text they had, on screen and on the clipboard.

## Answer

Words a reader needs:

- A **profile** rewrites a text in its own way, lighter or heavier. The profiles are those of the Rewriting page. Example: Affinage, Lissage, Arrangement.
- A **step** is a length from which one profile rewrites the dictation.
- A **block** is the stretch of text a profile rewrites in one go.
- A step **takes over** when the dictation reaches its length: it rewrites everything already said, in its own blocks, then goes on alone.
- The **text as said** is the transcript before any rewrite.
- **Stop** is the end of the dictation, when the user stops recording.
- The **transcript panel** is the panel that shows the dictation while the user speaks.

The examples use these settings: a first step Affinage from 1,000 characters in blocks of 600, a step Lissage from 4,000 in blocks of 2,000, a step Arrangement from 12,000 in blocks of 6,000.

### Setting it: the Rewriting page, section Dictation

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

### While the user speaks: the transcript panel

1. With "Rewrite while I speak" off, or before the first step is reached, the panel shows the text as said.
2. When the first step is reached with "Rewrite while I speak" on, the beginning is rewritten block by block. Each block changes in place, and the rest of the text does not move. The last block, not closed yet, stays as said.
3. When a higher step takes over, each block keeps its earlier text until its new version arrives, then changes in place. No block goes back to the text as said.
4. A block whose rewrite fails keeps what it showed.

### At Stop

1. Nothing is pasted: the text is on the clipboard, and the user puts it where they want.
2. With "Rewrite while I speak" on and a step reached: the step rewriting at Stop rewrites the block it had not finished, and the rewritten text is on the clipboard as soon as that block comes back. The user waits for that block only.
3. With it off and a step reached: the text as said is on the clipboard at once, and the whole text rewritten by the profile of the highest step reached replaces it when that profile is done. The HUD says so.
4. With no step reached: the text as said is on the clipboard.
5. If the rewrite fails, what was on the clipboard stays there, with the usual failure message.

### Rules

- A step's length is counted in characters of the text as said.
- A rewrite always starts from the text as said, never from an earlier rewrite.
- A dictation is always rewritten from its beginning, never from the length of the step. Example: 1,500 characters, first step at 1,000: all 1,500 characters are rewritten by Affinage.
- While the user speaks, one step rewrites at a time: the highest one reached. When a dictation reaches a step's length, that step rewrites everything already said, in its own blocks, then goes on alone, and the step before stops. Example: at 4,000 characters, Lissage rewrites the first 4,000 at once in two blocks, and Affinage sends nothing more.
- A block closes at the first pause once it reaches its step's block size.
- When a step takes over, it cuts the text already said along the blocks of the step before: each of its blocks joins whole earlier blocks until it reaches its size, and the last one may stay shorter. Example: at 4,000 characters, Lissage sends one block of about 2,500 and one of about 1,500. A step with smaller blocks than the one before resends that step's blocks as they are.

## Limits

- A block is rewritten without the blocks around it: a profile reorganizes inside a block, never across two. Rewriting at Stop is the only way to have a profile see the whole dictation.
- Taking over costs: each step that takes over sends again everything already said. With the example settings, a dictation of 15,000 characters sends about 31,000 characters in 15 requests while the user speaks, against 15,000 in one request at Stop.
- The speaking times on the timeline are orders of magnitude (700 characters ≈ 1 min), not measures.

## Open

1. **A block already read changes a second time while the user speaks.** The user reads the panel to find their place, reads the last segment, and sometimes scrolls up; whether a second change disturbs that is not known yet. Built today: a short cross-fade when animations are on. Other way, wanted by the user: a line-by-line rewrite animation in the HUD's colours. Depends on it: how the panel shows a block that takes over.
2. **The block left open at Stop.** Built today: the step rewriting at Stop rewrites it, and the user waits for that block, up to its block size. Against it: up to 6,000 characters to wait for with the example settings. To confirm in use. Depends on it: the wait at Stop.
3. **A block size suggested by the profile.** A profile could carry a block size that suits it (a light one 600, a heavy one 6,000), taken as the step's starting value when the profile is chosen. Built today: no suggestion; the block size stays on the step either way.
4. **A step's length: characters or speaking time?** Built today: characters, with the speaking time shown on the timeline only, since what makes a profile useful is the amount of text, and a silence must not make a step take over. Other way: speaking time.
5. **The last block cut when a step takes over.** Built today: sent as it is, shorter than the step's block size, since it was already closed. Against it: a short block gives a heavy profile little to reorganize. Other way: reopen it and let it grow with the speech, which the panel would then show as said again until it closes. To confirm in use.
