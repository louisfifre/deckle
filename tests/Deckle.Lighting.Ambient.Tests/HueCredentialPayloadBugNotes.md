# Hue credential payload bug notes

## A prefix of the stored Hue application key reached the log

- **Trigger:** Ambient Lighting storing the pairing result of a bridge, with Ambient detail logging enabled (Verbose).
- **Observed symptom:** `BridgePairingStoredDetail` carried `username_head`, the first characters of the application key. Established by reading the code.
- **Cause:** Same as the Lighting provider's `BridgePairedDetail`: a prefix of a credential was treated as harmless.
- **Violated invariant:** No secret reaches a log line, whole or in part. The event now carries the bridge id only.
- **Recurrence cue:** A payload named after a credential, or a shortened form of one, on any Ambient event.
- **Regression coverage:** `CredentialPayloadTests.NoHueEventCarriesAPayloadNamedAfterACredential` in this project, over `DeckleAmbientSource`.
