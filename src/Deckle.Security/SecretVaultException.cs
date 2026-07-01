namespace Deckle.Security;

// Raised when the vault cannot honour an operation against its backing file:
// an existing file that cannot be read or decrypted (wrong Windows account,
// corruption, a truncated write), or a write that failed to land. A missing
// file is NOT an error — it is an empty vault on first run — so this type is
// reserved for genuine anomalies the caller should surface, never for the
// ordinary "no such secret" case.
public sealed class SecretVaultException : Exception
{
    public SecretVaultException(string message) : base(message) { }
    public SecretVaultException(string message, Exception innerException)
        : base(message, innerException) { }
}
