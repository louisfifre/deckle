# Management gestures bug notes

## A refused or completed move to the bin left no line in the log, and an unreadable backlink search left no cause

- Trigger: an agent asks to move an object to the Anytype bin. The gesture refuses (the object is referenced, the references could not be read, the user declined, nobody answered) or the move goes through.
- Observed symptom: `DeleteRefused` was a Verbose event, so a default configuration kept no trace of a refusal; `ObjectBinned` was Informational without the Milestone tag, so the move itself was missing from the file a person reads later. When the backlink search failed, the refusal said `references_unreadable` and nothing about why.
- Cause: both events were written as detail, and the backlink count collapsed every failure (search error, object missing from the result) into a null.
- Violated invariant: a move that hides an object from every later read is told in the log, with its reason when it did not happen. A refusal carries the step and the error on one line.
- Fix: `DeleteRefused` and `ObjectBinned` carry the Milestone tag. `DeleteRefused` has `ex_type` and `cause` fields: the exception type and its bounded machine code, or `not_listed` when the search answered without the object. The reasons and causes live in `DeleteRefusalReasons` and `DeleteRefusalCauses`. No object name and no server text reaches the line.
- Recurrence cue: a bin refusal that shows only in a verbose session, or `references_unreadable` with an empty cause.
- Guarded by: `BinLogTests`.