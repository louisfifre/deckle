# MCP client token bug notes

## An empty vault entry was never re-minted

- **Trigger:** the vault held an entry for a client's token secret whose value was empty or only whitespace (for example after a write that stored an empty string), and Deckle booted.
- **Observed symptom:** the client could never authenticate: the snapshot skips empty tokens, so no bearer matched, and `EnsureMinted` kept reporting nothing to mint. Established by reproduction with a fake vault; how a real vault comes to hold an empty value was not observed, but `SecretVault.Set` only rejects `null`.
- **Cause:** `EnsureMinted` asked `Contains`, which is true for an entry that holds nothing, while `Authenticate` ignores such an entry. The two disagreed about what "present" means.
- **Violated invariant:** a token counts as present only when it holds a usable value, and the same test decides it for minting and for authentication.
- **Recurrence cue:** an existence check (`Contains`, `ContainsKey`) standing in for "has a usable value".

Regression: `McpClientTokensTests.EnsureMintedReplacesAnEmptyVaultEntry`.