namespace Deckle.Mail;

/// <summary>
/// The envelope of one received mail, as an outer layer read it from the
/// server. The core never parses a mail: it works on these given facts.
/// </summary>
/// <param name="Id">The Message-ID header, which also identifies the mail in the thread headers of others.</param>
/// <param name="MailboxId">The <see cref="Mailbox.Id"/> of the mailbox it was read from.</param>
/// <param name="DeliveredTo">The address it arrived through: the main address or one alias.</param>
/// <param name="From">The sender as the headers give it.</param>
/// <param name="ReceivedAt">When it reached the mailbox. Never stands for a deadline.</param>
/// <param name="SubjectLine">The Subject header, the sentence of a mail the sorting could not read.</param>
public sealed record Mail(
    string Id,
    string MailboxId,
    string DeliveredTo,
    MailSender From,
    DateTimeOffset ReceivedAt,
    string SubjectLine)
{
    /// <summary>The In-Reply-To header, or null.</summary>
    public string? InReplyTo { get; init; }

    /// <summary>The References header, as message ids.</summary>
    public IReadOnlyList<string> References { get; init; } = [];

    /// <summary>
    /// True when the mail carries a real file. An image that belongs to the body,
    /// a logo or the icons of a signature, does not count.
    /// </summary>
    public bool HasAttachment { get; init; }

    /// <summary>False when the sender failed the authenticity checks.</summary>
    public bool SenderVerified { get; init; } = true;

    /// <summary>Every message id that ties this mail to a thread of replies: its own and those it answers.</summary>
    internal IEnumerable<string> ThreadIds
    {
        get
        {
            yield return Id;
            if (InReplyTo is not null)
            {
                yield return InReplyTo;
            }

            foreach (var reference in References)
            {
                yield return reference;
            }
        }
    }
}

/// <summary>A sender: the address, and the name it gave itself, if any.</summary>
public sealed record MailSender(string Address, string? Name = null);
