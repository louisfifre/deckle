# MCP host observability bug notes

## A failed tool call was an Informational line with no cause and no pairing

- Trigger: an MCP client calls a tool that fails (handler exception, input or output refused by the schema, unknown tool).
- Symptom: the journal showed nothing. The single end line, `ToolCallFinished`, was Informational, so the always-on journal dropped it. It carried the exception type only, no start event and no identifier, and the status and machine code of an Anytype API refusal were lost.
- Cause: one event served every outcome, and only the level of success.
- Violated invariant: a failure is a Warning or an Error with its step and error; lasting work pairs its start and end with one identifier.
- Fix: `ToolCallStarted` (Verbose) and the two ends share a `work_id`. A call that gave the client its answer, or that the client withdrew, ends in `ToolCallCompleted`; every other outcome ends in `ToolCallFailed` (Warning) with the exception type, the API status and the bounded machine code (`AnytypeV2Exception.LogCode`). The exception message still goes to the client in the tool result and never to the log.
- Recurrence cue: a tool failure that appears in the client transcript and not in the journal.
- Guard: `McpObservabilityTests.AFailedToolCallReachesTheJournalWithItsTypeStatusAndCodeAndNeverItsMessage` and `EveryToolCallEndsWithTheWorkIdOfItsStart`.
- Interpretation, not measured: the start stays Verbose because a call is short and the rate limiter bounds the volume (60 requests per minute and client); a call that never ends is therefore not visible in the journal.

## A missing client token was a Verbose count

- Trigger: the vault holds no token for an MCP client when the environment variables are published.
- Symptom: that client is refused with `unknown-bearer` and nothing in the journal says why.
- Cause: `TokenVariablesPublished` is Verbose whatever its `missing` count.
- Fix: `TokenVariablesMissing` (Warning) is written next to it when `missing > 0`.
- Guard: `ClientsWhoseTokenIsMissingFromTheVaultAreAWarningInTheJournal`. The happy path of `MaterializeEnvironmentVariables` stays untested because it writes real User variables.

## A framework problem was a Warning once and Verbose forever

- Trigger: Kestrel or the MCP SDK reports the same Warning repeatedly (a client resetting connections, say).
- Symptom: the journal shows the first occurrence and never says that it went on.
- Cause: `McpHostLogBridge` kept a count that only fed Verbose events.
- Fix: `HostInternalLogCounted` (Journal) is written when the count reaches a power of ten and once more when the host's logging is disposed, if the count moved since.
- Guard: `AFrameworkProblemThatKeepsHappeningIsCountedInTheJournal`, `AProblemThatHappenedOnceNeedsNoCountWhenTheHostCloses`.

## Starting a disposed host returned false without a word

- Trigger: `McpHttpHost.Start()` after `DisposeAsync()`.
- Symptom: `false`, and the log shows neither a start nor a failure.
- Cause: the disposed branch returned before any event.
- Fix: `HostStartRefused` (Warning, Journal) with reason `disposed`.
- Guard: `StartingADisposedHostSaysWhyItDidNotStart`.