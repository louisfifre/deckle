namespace Deckle.Mail;

/// <summary>The three places a new mail lands in; each mail lands in exactly one.</summary>
public enum MailPlace
{
    /// <summary>It asks something of the person: a subject, one row of Triage.</summary>
    Triage,

    /// <summary>It asks nothing but states a fact that changes something: one thin line under the list.</summary>
    Information,

    /// <summary>It asks nothing and changes nothing, so it waits for the end of the day to go to the trash.</summary>
    SetAside,
}

/// <summary>Why a mail was set aside: the framed word of its line in the sorting's part of Activity.</summary>
public enum SetAsideReason
{
    Newsletter,
    Promotion,
    Receipt,
    NoActionNeeded,
}

/// <summary>Why the sorting could not read a mail: the framed label of its row.</summary>
public enum UnreadableReason
{
    ImagesOnly,
    Encrypted,
}
