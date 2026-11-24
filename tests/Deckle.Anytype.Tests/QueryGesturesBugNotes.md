# Query gestures bug notes

## update could not empty a date: a null or blank value went out as an empty string and the API answered 400

- Trigger: an agent calls `update` on a task with `{"due_date": null}` or `{"due_date": ""}` to remove its planned day.
- Observed symptom: the Anytype API answered 400 `invalid date format for "due_date": ` and the date stayed. No date property could be emptied through `update`.
- Cause: `BuildEntryAsync` turned every date value into a string through `AsString`, which maps null to `""`. The v1 API (anytype-heart 0.51.6, `core/api/service/property.go`) writes a property whose value is JSON null as « no value », but sends any string, an empty one included, through its date parser, which refuses it.
- Violated invariant: a date property can be emptied. Emptying is a JSON null on the wire, never an empty string; the comment on `AsDateOrClear` in `QueryGestures.cs` holds it at the line.
- Fix: `AsDateOrClear` sends null for a null or blank value and the string as given otherwise. A live trial on a throwaway task (2026-11-24) saw null and `""` remove the date and a real date set it again.
- Recurrence cue: a 400 `invalid date format` with nothing after the colon, or another nullable property kind (select, number) that cannot be emptied either. Those kinds still coerce null to `""`, `0` or `false`; only the date was fixed and verified.
- Guarded by: `QueryGesturesTests.UpdateWithANullOrBlankDateClearsItWithJsonNull`.
