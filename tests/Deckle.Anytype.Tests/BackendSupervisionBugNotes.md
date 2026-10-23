# Backend supervision bug notes

## Restart handoff starts a duplicate serve

- **Trigger:** A post-build self-restart is requested while the outgoing Deckle process's fire-and-forget Anytype initialization is still awaiting its first health probe. Shutdown disposes the supervisor, releases resident ownership, and starts the replacement process, but the initialization continues and spawns the backend after `ShutdownRequested`. The replacement reaches its own Anytype initialization while that backend is still warming up.
- **Observed symptom:** The replacement supervisor's first health probe fails, so it spawns a second `serve`. Shortly afterward the health endpoint answers `200` from the original process, the duplicate exits with code `1`, and the replacement supervisor logs `ready` followed immediately by `stopped`. It then repeats the duplicate spawn on the capped one-minute backoff while the original backend remains healthy.
- **Cause:** Two lifetime gaps composed. First, `BackendSupervisor.Dispose` cancelled only the watch token; it neither cancelled nor waited for an in-flight `EnsureRunningAsync`, which used the caller's default token and could therefore spawn after application shutdown began. Second, readiness was correlated only with the fixed health endpoint, not with the process being supervised. During the handoff, the replacement supervisor attributed the original backend's successful health response to its duplicate process and started watching the duplicate after that process had already exited.
- **Violated invariant:** Shutdown must prevent an in-flight initialization from acquiring a new process after its lifecycle owner is disposed. A supervisor may report `Started` and supervise a PID only when readiness belongs to that same live process. One healthy backend must result in one supervised `serve`, including across a Deckle restart.
- **Recurrence cue:** Two Deckle session identifiers overlap during startup; the earlier session attaches the long-lived PID, while the later session attaches a different PID and then emits repeated `BackendStarting` / health `200` / exit code `1` sequences at the restart-backoff cadence. Near-zero logged uptime is also a cue because the current uptime clock begins only after readiness, not at process spawn.

- **Regression coverage:** `BackendReconcilerTests.Successor_adopts_warming_backend_without_spawning_duplicate` holds the first provider in warm-up through deterministic gates, starts the successor reconciliation against the unbound endpoint, then publishes the first provider as the listener owner. It asserts that the successor adopts that PID and performs no spawn. `BackendSupervisorTests.Disposal_cancels_and_drains_inflight_reconciliation` separately pins the outgoing lifetime boundary: disposal does not return until the in-flight acquisition has observed cancellation.

## Caller cancellation poisoned the shared supervisor lifecycle

- **Trigger:** The first caller stopped waiting while the supervisor's one cached initialization was still reconciling, or that reconciliation faulted before supervisor disposal.
- **Observed symptom:** Every later caller received the same cancelled or faulted initialization task. On a non-cancellation fault, `DisposeAsync` exited before disposing the health probe and lifetime token source.
- **Cause:** The cached initialization was created with the first caller's token, making caller lifetime and supervisor lifetime the same boundary. Disposal awaited faultable owned tasks before resource cleanup without a `finally` boundary, and concurrent disposal callers did not share the in-flight drain.
- **Violated invariant:** One caller may abandon its wait without cancelling shared backend ownership. Owned resources must be released even when initialization or monitoring fails, and every disposal caller must observe the same completed drain.
- **Recurrence cue:** `EnsureRunningAsync` again passes a caller token into the cached work, `DisposeAsync` awaits owned work before entering guaranteed cleanup, or an already-disposing call returns without awaiting the cached disposal task.
- **Regression coverage:** `BackendSupervisorTests.Caller_cancellation_does_not_cancel_shared_initialization`, `BackendSupervisorTests.Faulted_initialization_cannot_bypass_owned_resource_cleanup`, and `BackendSupervisorTests.Concurrent_disposals_share_the_same_lifecycle_drain` pin the three lifecycle boundaries.

## Lost listener identity left the credentialed data plane open

- **Trigger:** A trusted backend exited after the MCP gateway had started, or startup returned without a proven provider while the fixed REST port was later occupied by another process.
- **Observed symptom:** The gateway remained available and `AnytypeApiClient` attached the bearer to every request solely from its fixed base URL, even after reconciliation reported an endpoint conflict.
- **Cause:** Listener ownership was proved only while adopting or spawning a backend. The credentialed REST boundary did not re-check that proof before sending.
- **Violated invariant:** Deckle must never send the headless Anytype bearer unless the fixed listener is currently owned by a live executable in the trusted provider catalog. Supervisor and HTTP-host cleanup must also complete independently so one teardown failure cannot release resident ownership early. The legacy Desktop pairing remains a separate, unsupervised compatibility path.
- **Recurrence cue:** A credentialed `HttpClient.SendAsync` path lacks an immediately preceding provider-owner check, or MCP drain failure can bypass supervisor disposal.
- **Regression coverage:** `AnytypeApiClientReplayTests.LostEndpointTrustPreventsTheBearerRequestFromBeingSent` proves that no HTTP request reaches the endpoint after trust is lost. The runtime cleanup ordering is kept explicit in the application composition root.

## Wedged serve read as nothing running

- **Trigger:** After a machine reboot, the logon-task Deckle spawned `serve`, and that serve stayed alive with its gRPC ports bound but never bound the REST listener. Its machine interface (`anytype --json inspect`) reported `account_transition`, not authenticated, and no account id: the auto-login stopped before account selection and never resumed. Why it stalled is inside anytype-cli and is not established; Deckle did not cause it.
- **Observed symptom:** The startup reconciliation timed out after its readiness window. Every recovery attempt then logged a reconciliation with trigger `process-exit`, decision `timed-out` and `pid=0`, on the restart backoff, for as long as Deckle ran, while the same serve PID stayed alive. Read alone, the journal said a process had exited and nothing was running, the opposite of the machine's state.
- **Cause:** Three facts compose. The reconciler never spawns while a trusted process is alive, so each attempt waited on the same wedged serve again; it released only its handle on timeout and never ended the process. `BackendReconciliationCompleted` logged `result.Process?.Id ?? 0`, and a timed-out result carries no process, so the live serve's PID was dropped. `BackendSupervisor.RestartAsync` passed the hard-coded trigger `process-exit` to every reconciliation, including the recovery that `RecoverAsync` opens after a start that never served.
- **Violated invariant:** A journal line describes the machine. A reconciliation that waited on a live serve names that serve, and a reconciliation names what opened its incident. The anti-duplicate rule still holds: a live trusted serve is never joined by a second one.
- **Recurrence cue:** A `timed-out` reconciliation with `pid=0` while `anytype.exe` is alive, a recovery labelled `process-exit` with no `BackendStopped` before it, or retries that repeat forever with no change in the process list. Deckle still has no bounded policy for a serve that stays alive and unready, so retries cannot recover from this state by themselves; only ending that serve does. Separately, a non-elevated Deckle cannot read an elevated orphan (`Access is denied`), treats it as absent and spawns duplicates that exit with code `1`.
- **Regression coverage:** `BackendReconcilerTests.Timed_out_reconciliation_names_the_live_serve_it_waited_on` asserts that the timed-out result names the live serve's PID and leaves it running. `BackendSupervisorTests.Recovery_after_a_start_that_never_served_is_not_reported_as_a_process_exit` asserts the recovery's trigger is `start-failed`. No test covers ending a wedged serve, because Deckle has no such policy yet.
## One backend outage wrote hundreds of journal lines

- **Trigger:** The headless backend dies or never serves, and the supervisor climbs its restart ladder (2, 5, 15, then every 60 s) while each serve it spawns exits or times out.
- **Observed symptom:** The journal of one outage held about 280 lines (141 and 140 of the two per-attempt events, as counted by the auditor from a real journal; not recounted here). The in-code comment promised one incident closed by one recovery.
- **Cause:** The reconciler wrote its start (`BackendStarting`), the serve's death (`BackendStopped`, a Warning), a timeout (`BackendStartTimedOut`, a Warning) and a failed spawn (`BackendSpawnFailed`, an Error) on every attempt, whatever had asked for the reconciliation. Only `BackendRestartsFailing` and `BackendRecovered` were limited to once per incident, so every rung added lines to the journal. No id tied the incident's lines together.
- **Violated invariant:** A degradation that repeats is one incident: one line opens it, one marks a streak past the threshold, one closes it, and all carry the same id. The attempts of the ladder are detail.
- **Recurrence cue:** An event written from the reconciler without looking at its trigger, or a new journal-tagged event on a path the ladder runs again; a `BackendRestartsFailing` or `BackendRecovered` without `work_id`.
- **Regression coverage:** `BackendRestartIncidentTests.ARestartAttemptWhoseServeDiesAgainWritesNoJournalLine` pins that a reconciliation triggered by the ladder writes no journal-kept line. `ABackendThatRejectsTenRestartsLeavesOneIncidentInTheJournal` and `ADeathThatEndsInAGiveUpClosesTheIncidentWithItsId` pin the one-opener, one-mark, one-closer shape and the shared id. `AFirstStartWhoseServeDiesPairsItsStartAndItsStopInTheJournal` keeps the first start in the journal. The 280-line count and the real ladder timing are not reproduced; the reconciliation runs against fakes.

## A foreign endpoint owner was logged with its full path

- **Trigger:** A program that is not a trusted Anytype provider holds the REST endpoint when the reconciler adopts or spawns.
- **Observed symptom:** The conflict detail and the listener observation carried the owner's full executable path, which embeds the Windows user name (a reading of the code, not a captured journal).
- **Cause:** Both events were given `owner.ExecutablePath` unchanged.
- **Violated invariant:** A line describes the machine with paths relative to a known root. The path of a program outside Deckle's catalog has no such root, so its file name is the most the log keeps.
- **Recurrence cue:** An event written with an `ExecutablePath` or other absolute path of a process Deckle does not own.
- **Regression coverage:** `BackendOwnerPathTests.AnUntrustedEndpointOwnerIsNamedByItsFileNameAlone`. The trusted-owner path of `BackendListenerObserved` is changed the same way but not asserted separately.

## A failed read of the listener table ended supervision for the session

- **Trigger:** `GetExtendedTcpTable` returned an error (or kept changing size) during a reconciliation, even once.
- **Observed symptom:** the reconciliation reported an endpoint conflict, and the supervisor, which treats a conflict as final, gave up restarting the backend until Deckle restarted. Established by reproduction with a scripted listener whose first inspection fails; whether the call fails in practice, and why, was not observed.
- **Cause:** `Failed` (the table could not be read) was mapped to the same outcome as `Ambiguous` (several processes own the endpoint). The first says nothing about the endpoint; the second is a verdict on it.
- **Violated invariant:** only a conclusion about the endpoint (an owner that is untrusted, unreadable or ambiguous) is a conflict; a failure to observe is retried, and stays a retryable outcome when it persists.
- **Recurrence cue:** an error state of an observation folded into the verdict states of what is observed, or a terminal outcome decided from a single sample.

A persistent failure now ends as `TimedOut` with the cause in its decision, so the supervisor keeps backing off and its journal line says the table could not be read. Nothing is spawned while the owner is unknown.

Regression: `BackendReconcilerTests.Transient_listener_inspection_failure_is_inspected_again` and `BackendReconcilerTests.Persistent_listener_inspection_failure_stays_retryable_and_spawns_nothing`.
