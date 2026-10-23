# Sink callback bug notes

## A sink swallowed the failure of the code it calls

- **Trigger:** a log-window surface, the HUD receiver, the application-log settings reader or filter, or a telemetry consent reader that throws. Reproduced 2026-10-22 by test, one case per sink.
- **Observed symptom:** the surface, the HUD flash, the `app.jsonl` mirror or a telemetry dataset stopped receiving rows, and nothing in the log said so. A consent reader that failed also looked exactly like a consent switched off.
- **Cause (established by running the tests):** `LogWindowSink.Write` and its replay, `HudFeedbackSink.Write`, `ApplicationLogSink.Wants` and `TelemetryListenerBootstrap.ReadGate` each wrapped the call in a bare `catch` that returned or continued. The dispatcher already contains a sink that throws and reports it as `SinkFaulted` then `SinkRecovered`, but it never saw these failures.
- **Violated invariant:** a degradation is reported once with its cause. The sink does not decide a failure is harmless; the dispatcher contains it.
- **Recurrence cue:** a sink or surface with no rows after some point and no `SinkFaulted` naming it. `LogWindowSink` still tries every surface before it hands the first failure on, so one broken surface does not starve the others.
- **Not covered:** the shutdown path of `TelemetryListenerBootstrap.ShutDown`, which now emits `FlushFailed` when a dataset sink cannot dispose. `BoundedWriteQueue.Dispose` gives no seam to make it fail from a test.

Regression: `SinkCallbackFaultTests`, `HudFeedbackSinkTests` (in `Deckle.App.Tests` since the sink moved to the host), `OperationalSinkRoutingTests.ApplicationLogDoesNotTakeAFailingSettingsReadForADisabledLog`, `TelemetryListenerBootstrapTests.AConsentReaderThatThrowsWritesNothingAndIsReportedAsASinkIncident`.
