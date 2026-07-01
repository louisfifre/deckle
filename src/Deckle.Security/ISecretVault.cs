namespace Deckle.Security;

// ── ISecretVault ──────────────────────────────────────────────────────────
//
// Deckle's home for every secret it must persist: the Anytype API key, the
// per-client MCP tokens, future third-party keys (transcription, rewrite).
// A named-value store — arbitrary, caller-owned names to opaque string values
// — kept sealed at rest so only the current Windows user account can read it.
//
// This is the seam the rest of Deckle depends on; the concrete store
// (SecretVault) owns the sealing mechanism. Consumers never learn how a secret
// is protected, only that it is. The boundary with the Windows Credential
// Manager is deliberate: the anytype-cli account key lives there (go-keyring
// owns it); everything Deckle itself mints or holds lives here. Two stores,
// split by which subsystem owns the secret — not an inconsistency.
public interface ISecretVault
{
    /// <summary>
    /// Reads the secret stored under <paramref name="name"/>. Returns
    /// <see langword="true"/> with the value when present; <see langword="false"/>
    /// with a null value when nothing is stored under that name.
    /// </summary>
    /// <exception cref="SecretVaultException">
    /// The vault file exists but cannot be read or decrypted.
    /// </exception>
    bool TryGet(string name, out string? value);

    /// <summary>Whether a secret is stored under <paramref name="name"/>.</summary>
    /// <exception cref="SecretVaultException">
    /// The vault file exists but cannot be read or decrypted.
    /// </exception>
    bool Contains(string name);

    /// <summary>
    /// Stores — or overwrites — the secret under <paramref name="name"/>.
    /// Durable when the call returns: the write is synchronous and atomic.
    /// </summary>
    /// <exception cref="SecretVaultException">The write could not be completed.</exception>
    void Set(string name, string value);

    /// <summary>
    /// Removes the secret stored under <paramref name="name"/>. Returns
    /// <see langword="true"/> when a secret was removed, <see langword="false"/>
    /// when nothing was stored under that name.
    /// </summary>
    /// <exception cref="SecretVaultException">The write could not be completed.</exception>
    bool Remove(string name);
}
