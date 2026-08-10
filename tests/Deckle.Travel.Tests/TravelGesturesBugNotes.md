# Travel gesture bug notes

## Update accepted a mutable display name as its replay coordinate

- **Trigger:** A Travel update renamed an object addressed by its old display name, then its response became ambiguous and the call was replayed.
- **Observed symptom:** The same request could no longer resolve its target after the first attempt had renamed it.
- **Cause:** The batch resolved name-or-id selectors even though update is advertised as replayable only with a stable target.
- **Violated invariant:** Every item in a replayable Travel update batch must carry its Anytype object id.
- **Recurrence cue:** A Travel update item accepts a display name or resolves its target before validating provider identity.

Regression coverage: `TravelUpdateTests.UpdateRequiresStableObjectIdsBeforeReadingOrWriting`.
