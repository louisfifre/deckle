# Lexicon bug notes

## Reset all on the Transcription page emptied the person's word list, even while it was hidden

- Trigger: v0.144.0, the Lexicon fold on the Transcription page, then « Reset all » in the page header. Found by a review pass reading the code (2026-11-22), not reported from use.
- Observed: not observed running. Established from the code: `WhisperPage.ResetAll_Click` resets every composer of the page, the lexicon's included, and the reset is not filtered by visibility, so the list was erased under Whisper too, where the fold is hidden.
- Cause: the Words card of the lexicon manifest declared `defaultValue: () => ""`. A composed card with a default gets a reset, and joins the page's Reset all.
- Violated invariant: a list the person authors is data, not a setting with a default; no reset ever empties it. The trap lives as a comment in `WhisperViewModel.Lexicon.cs` (the Words card declares no default).
- Fix: the lexicon moved to its own page, Transcription > Lexicon, with no Reset all, and its Words card declares no default.
- Recurrence cue: a `Setting.Text` or list card holding the person's own entries with a `defaultValue`; a page Reset all that a person reports « lost my words ».
- Guard: none automated. `WhisperViewModel` reads `TranscriptionSettingsService.Instance` in `Load`, and no Views test suite exists for this module, so asserting the manifest needs a seam the design does not want. The self-check capture `settings-lexicon` shows the page with no Reset all; a reviewer checks the card carries no reset button.

## A one-letter term recased every occurrence of that letter

- Trigger: a lexicon holding a single capital letter, such as « A ».
- Observed: not observed running. Established from the code: `LexiconTerms.Merge` had no minimum length, `LexiconRecaser` then rewrote every whole-word « a » (the French verb, everywhere) as « A », and `LexiconBoostTree` gave the one-letter key its full bonus.
- Cause: only empty and over-long entries were dropped.
- Violated invariant: a term shorter than `LexiconTerms.MinTermLength` never reaches the casing pass or the boost; the reason is the comment on that constant.
- Fix: `Merge` drops terms shorter than two characters.
- Recurrence cue: a dictation whose every « a » or « y » comes out capitalized; a change to `MinTermLength` or to the filter in `Merge`.
- Guard: `LexiconTermsTests.AOneLetterTermIsDropped`.
