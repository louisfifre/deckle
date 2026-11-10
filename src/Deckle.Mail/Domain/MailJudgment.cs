namespace Deckle.Mail;

/// <summary>
/// What the sorting model concluded about one mail. Judging is the model's
/// work, not the core's: the core takes these facts as given and applies the
/// rules of the spec to them (doubt, unread mail, unverified sender,
/// corrections, subjects, order).
/// </summary>
public sealed record MailJudgment
{
    private MailJudgment(MailPlace place, string? sentence)
    {
        Place = place;
        Sentence = sentence;
    }

    /// <summary>The place the model chose, before the core's rules apply.</summary>
    public MailPlace Place { get; }

    /// <summary>
    /// The sentence of the row or line, written once, in the language of the mail.
    /// Null only for a mail that could not be read.
    /// </summary>
    public string? Sentence { get; }

    /// <summary>The deadline the text states, or null when it states none.</summary>
    public Deadline? Deadline { get; private init; }

    /// <summary>Set for a mail set aside.</summary>
    public SetAsideReason? SetAsideReason { get; private init; }

    /// <summary>
    /// True when the model is not sure the mail can be set aside. Such a mail
    /// waits in Triage instead: a wrong set-aside is silent, a wrong row is not.
    /// </summary>
    public bool Doubtful { get; private init; }

    /// <summary>True when the mail only repeats a request an earlier mail of its subject made.</summary>
    public bool RepeatsRequest { get; private init; }

    /// <summary>Set when the model could not read the mail.</summary>
    public UnreadableReason? Unreadable { get; private init; }

    /// <summary>
    /// The order, parcel or file numbers found in the mail. Two mails that carry
    /// the same number are one subject.
    /// </summary>
    public IReadOnlyList<string> MatterNumbers { get; init; } = [];

    /// <summary>A mail that asks something of the person, with the deadline its text states, if any.</summary>
    public static MailJudgment Asks(string sentence, Deadline? deadline = null) =>
        new(MailPlace.Triage, Required(sentence)) { Deadline = deadline };

    /// <summary>
    /// A mail that asks something within a time slot. The deadline is the end
    /// of the slot. Example: a delivery between 9:00 and 12:00 on Thursday has
    /// its deadline on Thursday at 12:00.
    /// </summary>
    public static MailJudgment AsksWithin(string sentence, Deadline slotStart, Deadline slotEnd)
    {
        ArgumentNullException.ThrowIfNull(slotStart);
        ArgumentNullException.ThrowIfNull(slotEnd);
        return Asks(sentence, slotEnd);
    }

    /// <summary>A later mail of a subject that only repeats its request: the row keeps the sentence that asks.</summary>
    public static MailJudgment RepeatsEarlierRequest(string sentence) =>
        new(MailPlace.Triage, Required(sentence)) { RepeatsRequest = true };

    /// <summary>A mail that asks nothing but states a fact that changes something for the person.</summary>
    public static MailJudgment Informs(string sentence) =>
        new(MailPlace.Information, Required(sentence));

    /// <summary>A mail that asks nothing and changes nothing.</summary>
    public static MailJudgment SetAside(SetAsideReason reason, string sentence, bool doubtful = false) =>
        new(MailPlace.SetAside, Required(sentence)) { SetAsideReason = reason, Doubtful = doubtful };

    /// <summary>A mail the model could not read: it holds only images, or it is encrypted.</summary>
    public static MailJudgment CouldNotRead(UnreadableReason reason) =>
        new(MailPlace.Triage, sentence: null) { Unreadable = reason };

    private static string Required(string sentence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sentence);
        return sentence;
    }
}
