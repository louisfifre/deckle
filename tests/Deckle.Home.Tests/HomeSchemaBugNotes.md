# Home schema bug notes

## The Home schema incident logged the validation message and never told its recovery

- Trigger: the Home space does not conform to the schema (a property is missing, a type is absent), then the person repairs it.
- Observed symptom: the Verbose `SchemaRejectedDetail` carried `HomeSchemaException.Message`, which lists the schema keys the space lacks. The `SchemaReady` event that closed the incident was Informational without the Journal tag, so a default configuration kept the opening Warning and never its end, and nothing paired the two.
- Cause: the detail event took the exception text as is, and the closing event was written as ordinary information.
- Violated invariants: a provider that reads Home logs the error type and code, not text built from what the space holds. A degradation that repeats is one incident, closed by one recovery that travels in the journal.
- Fix: `SchemaRejectedDetail` carries the reason code and a `failed_checks` count (`HomeSchemaException.FailedChecks`). `SchemaUnavailable`, `SchemaAttemptFailed` and `SchemaReady` carry a shared `work_id`, and `SchemaReady` carries the Journal tag.
- Uncertain: the messages today name canonical manifest keys only, so the leak was a latent one rather than an observed one. The count is the cost of removing the doubt.
- Recurrence cue: a Home schema event whose payload holds free text, or an incident opened in the journal with no end.
- Guarded by: `HomeSchemaIncidentTests`.