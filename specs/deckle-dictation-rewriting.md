# Deckle — Dictation rewriting

Written on 2026-11-11 from the usage Louis settled that evening, revised on 2026-11-12 after mockups of the two modes (one switch for the whole list, the same lengths in both modes, a block size per step, one step rewriting at a time). The example settings used below: a first step Affinage starting at 1,000 characters in blocks of 600, a step Lissage from 4,000 in blocks of 2,000, a step Arrangement from 12,000 in blocks of 6,000.

## Intention

Louis dictates, often at length, and wants the text he gets back to read as written, with his words. A short dictation needs a light touch; a long one needs more of the work done. He sets once which profile takes a dictation at which length, and whether the rewriting happens while he speaks or once he stops; then he dictates without thinking about it.

## Journeys

**A dictation of a few sentences.** Louis presses the shortcut, speaks, stops. The panel showed what he said. The text is on the clipboard, rewritten by the profile of the first step.

**A long dictation, rewritten while he speaks.** Louis speaks for twenty minutes with "Rewrite while I speak" on. From 1,000 characters, the beginning of the panel turns into rewritten text block by block while he goes on. At 4,000, Lissage takes over: the text already shown is rewritten again by it, then it goes on alone. At 12,000, Arrangement does the same. At Stop he waits only for the block Arrangement had not finished, then the text is on the clipboard.

**A long dictation, rewritten at Stop.** With "Rewrite while I speak" off, the panel shows what he says. At Stop the text as said is on the clipboard at once, then the whole text rewritten by the profile of the highest step reached replaces it.

## Surfaces

### Rewriting page, section "Dictation"

1. With "Rewrite dictations" off, a dictation arrives as said, and nothing else of the section is shown.
2. With it on, the page shows "Rewrite while I speak", the timeline, one card per step, then "Add a step". The first step always exists and cannot be removed.
3. "Rewrite while I speak" appears only when transcription is streaming. Without streaming transcription, every step rewrites at Stop.
4. Every step, the first included, reads "From" and a length. A dictation shorter than the first step's length stays as said, and a first step from 0 rewrites every dictation from its beginning. Example: from 1,000, a dictation of 600 characters is not rewritten.
5. Every later step carries a length and a profile. A step added lands after the last one. No step carries a switch of its own for the moment it rewrites.
6. With "Rewrite while I speak" on, every card also shows "in blocks of" with a number of characters: the size of the stretch its profile rewrites in one go. Off, the block size is hidden and kept.
7. Switching "Rewrite while I speak" changes no length. Example: Arrangement from 12,000 stays from 12,000 in both modes.
8. A block size reaches at least 6,000 characters. Example: Arrangement in blocks of 6,000 is accepted as typed.
9. The timeline shows one thin row per step, the step's name in its row, in the system accent colour and neutrals only, with the same rows in both modes. Under the rows, a scale gives lengths with an approximate speaking time written with "≈" (12,000 ≈ 17 min).
10. Off, each row shows the range of lengths its step takes: from its length to the next step's.
11. On, each row shows its step's blocks, from the beginning of the dictation to where the next step takes over. The blocks before the step's own length are pale: the text it rewrites at once when it takes over. One caption under the timeline says so, in this mode only.
12. The timeline shows and never writes: the values are changed in the cards.
13. The words of the section are: Dictation, Rewrite dictations, Rewrite while I speak and its one-line description, From, characters, in blocks of, Add a step, and the timeline's caption. No card description.
14. The page has no "Rewrite profile" card, no "Switch profile by length" switch and no "Batch size" card.

### Transcript panel, while dictating

1. With "Rewrite while I speak" off, or before the first step, the panel shows the text as said.
2. When the first step is reached with "Rewrite while I speak" on, the beginning is rewritten block by block. Each block changes in place, the rest does not move. The last block, not closed yet, stays as said.
3. When a higher step takes over, each block keeps its earlier text until its new version arrives, then changes in place. No block goes back to the raw text.
4. A block whose rewrite fails keeps what it showed.

### Stop

1. Nothing is pasted. The text is on the clipboard.
2. With "Rewrite while I speak" on and a step reached: the step rewriting at Stop rewrites the block it had not finished, and the rewritten text is on the clipboard as soon as that block comes back.
3. With it off and a step reached: the text as said is on the clipboard at once, and the rewritten version replaces it when the profile is done; the HUD says so.
4. If the rewrite fails, what was on the clipboard stays there, with the usual failure message.

## Behaviours

- A step's length is counted in characters of the text as said.
- A rewrite always starts from the text as said, never from an earlier rewrite.
- A dictation is always rewritten from its beginning, never from the length of the step. Example: 1,500 characters, first step at 1,000: all 1,500 characters are rewritten by Affinage.
- While Louis speaks, one step rewrites at a time: the highest one reached. When a dictation reaches a step's length, that step rewrites everything already said, in its own blocks, then goes on alone, and the step before stops. Example: at 4,000 characters, Lissage rewrites the first 4,000 at once in two blocks, and Affinage sends nothing more.
- A block closes at the first pause once it reaches its step's block size.

## Limits

- Automatic pasting is gone: the text always lands on the clipboard and Louis does what he wants with it.
- A block is rewritten without the blocks around it: a profile reorganizes inside a block, never across two. Rewriting at Stop is the only way to have a profile see the whole dictation.
- Taking over costs: each step that takes over sends again everything already said. With the example settings, a dictation of 15,000 characters sends about 31,000 characters in 15 requests while speaking, against 15,000 in one request at Stop.
- The speaking times on the timeline are orders of magnitude (700 characters ≈ 1 min), not measures.
- When a step takes over, it cuts the text already said along the blocks of the step before: each of its blocks joins whole earlier blocks until it reaches its size, and the last one may stay shorter. Example: at 4,000 characters, Lissage sends one block of about 2,500 and one of about 1,500. A step with smaller blocks than the one before resends that step's blocks as they are.

## Open

1. **Panel 3: a block already read changes a second time while Louis speaks.** Louis does not know yet (2026-11-11): he uses the panel to find his place, reads the last segment, sometimes scrolls up. Built for now: a short cross-fade when animations are on. A line-by-line rewrite animation in the HUD colours is wanted (2026-11-12) and is its own work.
2. **The block left open at Stop.** Proposed and built: the step rewriting at Stop rewrites it, and Louis waits for that block (up to its block size). Against it: up to 6,000 characters to wait for with the example settings. To confirm in use.
3. **A block size suggested by the profile.** Proposed on 2026-11-12: a profile may carry a block size that suits it (a light one 600, a heavy one 6,000), taken as the step's starting value when the profile is chosen. Not decided; the block size stays on the step either way.
4. **A step's length: characters or speaking time?** Proposed and built: characters, with the speaking time shown on the timeline only. What makes a profile useful is the amount of text, and a silence must not make a step cross.
5. **The last block cut when a step takes over.** Built (2026-11-12): sent as it is, shorter than the step's block size, since it was already closed (see Limits). Against it: a short block gives a heavy profile little to reorganize. The other way: reopen it and let it grow with the speech, which the panel would show raw again until it closes. To confirm in use.
