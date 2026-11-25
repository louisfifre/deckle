# Hue Entertainment bug notes

## Stopping Ambient left the room almost dark

- **Trigger:** Ambient ran on the Entertainment output (DTLS stream to an entertainment area), then the person stopped it. Reported on 2026-11-07 in a dark room with Windows in dark mode, so the screen, and the last streamed frames, were dark too.
- **Observed symptom:** after the stop the area's lights stayed at or near black and the person could not see the room. The log shows a normal end: `Hue Entertainment session closed` with outcome `completed`, so no step failed.
- **Cause:** just before `action=start`, `ConnectAsync` pre-primes every area light over REST to (1, 1, 1), a near-black that keeps the bulbs on so Hue's own power-on scene does not flash. When streaming stops, the bridge's own return, under the Hue app's default "When syncing stops: Last on-state", goes back to the state it saw when streaming started, which is that pre-prime. Under the other option, "Pause on the last colour", the bridge returns nothing and the lights keep the last dark frame. Established from the code and the log; the bridge's two behaviours come from other developers' measurements (Glowcast issues #142 and #181, LumaSync PR #486), since Hue's own Entertainment documentation sits behind a sign-in. Not reproduced on the bridge tonight.
- **Violated invariant:** after Ambient lets go of a light, the light shows what it showed before Ambient took it over. Whatever Deckle writes to a light to prepare a stream has to be undone by Deckle, because the bridge's own return restores Deckle's preparation, not the person's state. The trap is commented at the capture in `ConnectAsync` of `src/Deckle.Lighting/Hue/HueEntertainmentLightOutput.cs`; the capture and the restore themselves live in `HueLightStateRestorer`.
- **Recurrence cue:** a new write to the lights before `action=start` (a prime, a fade-in, a test colour), or a new output that streams, without a matching capture and restore. Also a restore written once, right after the stop: the bridge's own post-stream write can land after it (0.3 to 2 s in the reports above) and has to be outlasted.

Regression: `HueEntertainmentRestoreRegressionTests` (four cases: plain stop, the bridge's late return to the pre-prime, a failed start, a light that was off).

The REST fallback that also drove the lights (`HueRestLightOutput`) had the same gap and got the same restore. It left on 2026-11-25, when Ambient became Entertainment only, with its regression tests.

Not covered: nothing here has run on a real bridge.