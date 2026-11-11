namespace Deckle.Mail;

/// <summary>
/// What an Undo on a line of the sorting's part teaches it: the next mails of
/// that kind from that sender are not set aside but wait in Triage.
/// </summary>
/// <remarks>
/// Built through <see cref="For"/> only, so the address is compared without case,
/// as mail providers do for the domain and in practice for the local part.
/// </remarks>
public sealed record SortingCorrection
{
    private SortingCorrection(string senderAddress, SetAsideReason reason)
    {
        SenderAddress = senderAddress;
        Reason = reason;
    }

    public string SenderAddress { get; }

    public SetAsideReason Reason { get; }

    public static SortingCorrection For(string senderAddress, SetAsideReason reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senderAddress);
        return new SortingCorrection(senderAddress.Trim().ToLowerInvariant(), reason);
    }
}
