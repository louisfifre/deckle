namespace Deckle.Mail.Hub;

// What a page of the Mail window reaches of its window: the session it shows
// and the way to another page. A page holds no reference to the window
// itself, so it can be read and tested apart from it.
public interface IMailShell
{
    TriageSession Session { get; }

    void Go(MailDestination destination);

    void GoBack();
}

// The parameter every page receives through Frame.Navigate.
public sealed record MailPageArgs(IMailShell Shell, MailDestination Destination);
