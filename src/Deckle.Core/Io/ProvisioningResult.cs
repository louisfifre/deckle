namespace Deckle.Core;

public sealed record ProvisioningResult(
    bool Success,
    string? ErrorMessage,
    long? Bytes,
    string? Sha256 = null)
{
    public static ProvisioningResult Ok(long? bytes, string? sha256 = null) =>
        new(true, null, bytes, sha256);

    public static ProvisioningResult Fail(string message) =>
        new(false, message, null);
}
