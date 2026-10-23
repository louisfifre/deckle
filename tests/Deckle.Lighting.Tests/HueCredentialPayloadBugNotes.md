# Hue credential payload bug notes

## A prefix of the Hue application key reached the log

- **Trigger:** Pairing a Hue bridge, with Hue detail logging enabled (Verbose, pairing keywords).
- **Observed symptom:** `BridgePairedDetail` carried `username_head`, the first characters of the application key issued by the bridge. Established by reading the code.
- **Cause:** The event was written to confirm that a key had been received, and a prefix looked harmless. A prefix of a credential is still part of the credential, and it makes a leaked log narrow a brute-force search.
- **Violated invariant:** No secret reaches a log line, whole or in part; a log says whether a credential exists (`client_key_present`), never what it looks like.
- **Recurrence cue:** A payload named `username`, `*_head`, `*_prefix`, `client_key` or `application_key` on a Hue event; a "short form for display" of a credential passed to a logger.
- **Regression coverage:** `CredentialPayloadTests.NoHueEventCarriesAPayloadNamedAfterACredential` reads the provider's manifest and fails on any payload named after a credential. Boolean `*_present` flags are allowed. The event id (9) is retired and never reused.
