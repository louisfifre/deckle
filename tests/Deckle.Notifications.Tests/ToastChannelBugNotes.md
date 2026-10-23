# ToastChannel bug notes

## A toast that could not be withdrawn left no trace

- **Trigger:** a prompt is abandoned (its token is cancelled) after its toast was shown, and the platform refuses `RemoveByTagAsync`, for example because the notification service is stopped.
- **Observed symptom:** the toast stayed on screen and in the Notification Center offering buttons that did nothing, and the log held nothing about it. Confirmed by reading the code: the `catch (Exception)` in `WithdrawAsync` was empty. Not reproduced against a real platform failure.
- **Cause:** the failure was judged harmless because the orphaned activation is ignored, so the catch swallowed it without a line.
- **Violated invariant:** a failure is reported with its cause, and a degradation that repeats is one incident closed by one recovery.
- **Recurrence cue:** a `Deckle-Notifications` Warning "A toast could not be withdrawn" with no later "Toast withdrawal works again".
- **Not covered:** `ToastChannel` needs the platform's `AppNotificationManager`, so no test drives the real withdrawal. The rule is tested through `ToastWithdrawIncident`; that `WithdrawAsync` calls it on each outcome is read from the code, not run.

Regression: `ToastWithdrawIncidentTests.Withdrawals_that_keep_failing_stay_one_incident_closed_by_one_recovery`.
