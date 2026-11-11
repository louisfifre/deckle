# Deckle — Dictation rewriting

Written on 2026-11-11 from the usage Louis settled that evening (second version of the draft, after his answers on the first one). The example settings used below: a first step Affinage, a step Lissage from 4,000 characters, a step Arrangement from 12,000 characters with Streaming off.

## Intention

Louis dictates, often at length, and wants the text he gets back to read as written, with his words. A short dictation needs a light touch; a long one needs more of the work done. He sets once which profile takes a dictation at which length, and whether it works while he speaks or once he stops; then he dictates without thinking about it.

## Journeys

**A dictation of a few sentences.** Louis presses the shortcut, speaks, stops. The panel showed what he said. The text is on the clipboard, rewritten by the profile of the first step.

**A long dictation.** Louis speaks for twenty minutes. Once the first streaming step is reached, the beginning of the panel turns into rewritten text passage by passage while he goes on. When a higher streaming step is reached, the passages already shown are rewritten again by its profile and change in place. Past 12,000 characters he goes on speaking; at Stop the passage version is on the clipboard at once, then the whole text rewritten by Arrangement replaces it.

## Surfaces

### Rewriting page, section "Dictation"

1. With "Rewrite dictations" off, a dictation arrives as said, and neither the timeline nor the steps are shown.
2. With it on, the page shows the timeline, then one card per step, then "Add a step". The first step always exists and cannot be removed.
3. The first step starts at the beginning of the dictation. With "Start at" ticked, it starts at the given length, and a shorter dictation stays as said. Example: at 1,000, a dictation of 600 characters is not rewritten.
4. Every later step carries a length, a profile and a "Streaming" switch. A step added lands after the last one.
5. With "Streaming" on, the step's profile rewrites while the person speaks, passage by passage. Off, its profile rewrites the whole text at Stop.
6. A dictation is always rewritten from its beginning, never from the length of the step. Example: 1,500 characters, first step at 1,000: all 1,500 characters are rewritten by Affinage.
7. The timeline has two lanes, "Streaming" and "At stop". Each step sits on its lane, at its length, with an approximate speaking time. The timeline shows and never writes: the values are changed in the cards.
8. When transcription is not streaming, the steps' "Streaming" switch does not appear and every step rewrites at Stop.
9. The only words of the section are: Dictation, Rewrite dictations, Streaming, At stop, As said, Start at, From, characters, Add a step. No caption and no card description.
10. The page has no "Rewrite profile" card, no "Switch profile by length" switch, no "While you dictate" section and no "Batch size" card.

### Transcript panel, while dictating

1. Before the first step, or as long as no step reached is streaming, the panel shows the text as said.
2. When a streaming step is reached, the beginning is rewritten passage by passage. Each passage changes in place, the rest does not move. The last passage, not closed yet, stays as said.
3. When a higher streaming step is crossed, each passage keeps its earlier text until its new version arrives, then changes in place. No passage goes back to the raw text.
4. When a step "at Stop" is crossed, the panel goes on with the profile of the highest streaming step reached.
5. A passage whose rewrite fails keeps what it showed.

### Stop

1. Nothing is pasted. The text is on the clipboard.
2. The highest step reached is streaming: the rewritten text is on the clipboard as soon as the last passage comes back.
3. The highest step reached is "at Stop", after a streaming step: the passage version is on the clipboard at once. When the step's profile is done, its version replaces it, and the HUD says so.
4. The highest step reached is "at Stop", with no streaming step before it: the text as said is on the clipboard at once, and the rewritten version replaces it when the profile is done.
5. If that profile fails, what was on the clipboard stays there, with the usual failure message.

## Behaviours

- A step's length is counted in characters of the text as said.
- A rewrite always starts from the text as said, never from an earlier rewrite.

## Limits

- Automatic pasting is gone: the text always lands on the clipboard and Louis does what he wants with it.
- The size of a passage (about 600 characters, closed at the first pause) is not on the page; it stays in the settings file.
- The speaking times on the timeline are orders of magnitude (1,000 ≈ 1 min, 4,000 ≈ 5 min, 12,000 ≈ 15 min), not measures.

## Open

1. **Panel 3: a passage already read changes a second time while Louis speaks.** Louis does not know yet (2026-11-11): he uses the panel to find his place, reads the last segment, sometimes scrolls up. To be judged on a real dictation. Proposed: the passage changes in place, without animation and without moving the scroll. Built for now: the panel's existing rule, a short cross-fade when animations are on; the display rule changes without touching the engine.
2. **Panel 4: after a step "at Stop", keep streaming with the step before?** Proposed and built: yes. The panel stays readable, a text is usable from Stop, and it is the fallback if the profile fails. Against it: those passages are paid for, then replaced.
3. **A step's length: characters or speaking time?** Proposed and built: characters, with the speaking time shown on the timeline only. What makes a profile useful is the amount of text, and a silence must not make a step cross.
