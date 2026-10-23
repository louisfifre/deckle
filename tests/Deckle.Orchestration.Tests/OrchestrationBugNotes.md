# Orchestration observability bug notes

## The session and rule milestones carried no entry and stayed out of the log

- Trigger: an app session starts or ends, or its rules are applied or restored.
- Symptom: the log held no line for any of it. `SessionStarted`, `SessionEnded`, `ProfileApplied` and `ProfileRestored` were Informational without the Milestone tag, and had no payload, so even in the full log a milestone could not be matched to the entry whose Verbose detail came next.
- Cause: the milestones were written as bare markers and the identity was left to the Verbose detail.
- Violated invariant: lasting work writes a start and an end, both in the log, that share an identifier.
- Fix: the four milestones carry the Milestone tag and the entry id (`entry_id`), plus the pid, the end reason, the change count and the restored count. An entry has at most one session at a time, so the entry id pairs a start with its end and the rules applied with the rules restored.
- Recurrence cue: a `session started` line with no `session ended` line for the same `entry_id`, or an `App rules applied` line with no `App rules restored`.
- Guard: `OrchestrationIncidentTests.ASessionAndItsRulesPairByTheEntryTheyNameInTheLog`.
- Interpretation: `entry_id` is an opaque identifier (a GUID), not the app's name, so the log line carries no user content.

## Message templates dropped a payload

- Trigger: `SessionHandlerFailed`, `RuleApplyFailed` and `RuleRestoreFailed` fire.
- Symptom: the formatted line did not name the entry. The payload `entry_id` was in the structured event, and the template skipped its index, so the text a person reads said an app session event failed and not which one.
- Cause: the templates referenced the other payloads by index and left one out.
- Violated invariant: a line carries its step and its error on one line; every payload belongs to the line.
- Fix: the three templates name `entry_id`.
- Recurrence cue: a Warning from this provider whose text has fewer fields than the event has payloads.
- Guard: `OrchestrationIncidentTests.EveryPayloadOfTheProviderAppearsInItsMessageTemplate` checks every Verbose, Warning and Error event of the provider. An Informational template is a sentence alone since the template doctrine, and its values travel in the payload of the JSONL line.

## A subscriber or exit handler that kept throwing was a Warning per occurrence

- Trigger: the foreground subscriber, or the handler given to `ProcessExitWatch`, throws every time it is called.
- Symptom: one Warning for each focus change, or for each process exit, with no end: the log filled, and no line said that the failure had stopped.
- Cause: each failure was its own event, with no notion of an incident.
- Fix: `FailureIncident` tells the first failure from the repeats. The first is the Warning (`ForegroundDeliveryFailed`, `ExitWatchCallbackFailed`); the repeats are Verbose details; the next success writes `ForegroundDeliveryRecovered` or `ExitWatchCallbackRecovered`, both in the log.
- Recurrence cue: two Warnings of the same event with no `Recovered` line between them.
- Guard: `OrchestrationIncidentTests.AForegroundSubscriberThatKeepsFailingIsOneWarningThenOneRecovery` and `AnExitHandlerThatKeepsFailingIsOneWarningAndItsSuccessIsTheRecovery`. The second reaches `ProcessExitWatch` through a process id that no process has, which makes the exit be reported inline.
- Interpretation: the foreground incident is shared by every delivery of one monitor and the exit incident by every exit of one watch, so two different subscribers failing alternately would read as one incident. This is a simplification, not measured.

## The process-name tier matched an entry whose known executable was another file

- Trigger: two registry entries for two different programs that ship the same executable name (two games with a `game.exe`), one of them running.
- Symptom: the entry of the program that was not running got the session, so its rules were applied to the wrong program.
- Cause: after the executable-path tier and the install-directory tier found nothing, the process-name tier compared the name alone and never looked at the path the entry already knew.
- Violated invariant: the name is the weakest evidence and decides only where nothing stronger speaks; an entry that knows its file does not claim another file.
- Fix: the name tier admits an entry only when it knows no executable path, when the process path is unknown, or when the process lies inside the entry's install directory (an app moved to a versioned folder on update).
- Recurrence cue: a session started for an entry whose `ExecutablePath` is not the path of the process that triggered it.
- Guard: `AppMatcherTests.ProcessNameDoesNotMatchAnEntryWhoseKnownPathIsAnotherFile`, `ProcessNamePicksTheEntryWithoutAPathWhenAnotherNamesAnotherFile` and `AppMovedOutsideItsInstallDirectoryDoesNotMatchByName`; `AppMovedToAVersionedFolderStillMatchesThroughItsInstallDirectory` pins the case the fix must keep.
- Interpretation: an entry with a path and a process whose path could not be read still matches by name. That was kept on purpose, since an unreadable path proves nothing against the entry; it was not measured how often that happens.