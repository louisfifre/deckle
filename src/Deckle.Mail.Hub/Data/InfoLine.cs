namespace Deckle.Mail.Hub;

// A secondary information: a mail that asks nothing but states a fact that
// changes something for the person. One thin line under the list, its
// sentence then its sender in grey; it opens nothing.
public sealed record InfoLine(
    string Id,
    string MailboxId,
    string? AliasId,
    string Sentence,
    string Language,
    string Sender);
