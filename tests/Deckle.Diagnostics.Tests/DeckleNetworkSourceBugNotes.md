# Network source bug notes

## The network name travelled in the journal's status snapshot

- **Trigger:** the Network detail is on (Verbose, Network keyword) and Windows broadcasts a network change, or Deckle starts.
- **Observed symptom:** `NetworkStatusChanged` carried the profile name of the internet connection (for a Wi-Fi link, the name of the network). The same provider already kept that name out of `NetworkConnectivityChanged` for that reason. Confirmed by reading the code. The event is Verbose and opt-in, not always on.
- **Cause:** the snapshot was written before the rule "a Wi-Fi name can identify a place" was applied to the connectivity line, and was not revisited.
- **Violated invariant:** a line describes the machine, never what identifies a person or a place.
- **Recurrence cue:** a field of `NetworkStatusChanged` that holds free text from the OS.
- **Fix:** the field is now `medium`, a closed vocabulary (`NetworkMedium`) read from the IANA interface type of the adapter.

Regression: `DeckleNetworkSourceTests.NetworkStatusChangedHasNoFieldThatCouldHoldANetworkName`.

## A network query that threw was written as "offline"

- **Trigger:** a WinRT call in `NetworkStatusEmitter` throws (`GetInternetConnectionProfile`, or `GetNetworkConnectivityLevel` on a profile removed meanwhile).
- **Observed symptom:** the emitter fell back to state `none` and gave that to the tracker, so the always-on journal could say "went offline" for a machine that was online. The failure itself was a Verbose line that does not travel. Confirmed by reading the code; no throwing WinRT call was observed in a real run.
- **Cause:** one fallback served two different facts, "no internet profile" and "the profile could not be read".
- **Violated invariant:** a state that nobody observed is not recorded as a state; a degradation is a Warning with its cause.
- **Recurrence cue:** a `NetworkConnectivityChanged ... state=none` with no `NetworkQueryFailed` before it, or a Warning `NetworkQueryFailed` followed by a flip that no adapter event explains.
- **Fix:** a throwing query leaves the tracker untouched and is reported once per stage as a Warning, closed by one `NetworkQueryRecovered`. The trade-off is interpretation, not observation: if the failing broadcast was the last one, the journal keeps the previous state until the next broadcast, and the Warning tells the reader to distrust that interval.
- **Guard:** the reporting is covered by `NetworkQueryIncidentsTests`. The emitter itself calls WinRT and has no seam, so the "tracker untouched" half is not covered by a test.
