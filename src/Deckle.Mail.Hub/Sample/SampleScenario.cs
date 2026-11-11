namespace Deckle.Mail.Hub;

// The situations the built-in dataset can present, each named after the
// frame that draws it. Selected in code (MailWindow.UseScenario); no setting
// exposes it, since the dataset goes when the real engine is wired.
public enum SampleScenario
{
    // 2.1: a normal day, four subjects, three secondary lines.
    NormalDay,
    // 2.8: everything treated, nothing waiting.
    NothingWaiting,
    // 1.1: no mailbox yet.
    NoMailbox,
    // 7.9: Anytype not connected, so no Add task on the rows.
    AnytypeNotConnected,
    // 6.1: one mailbox cannot connect.
    MailboxCannotConnect,
    // 2.5: a normal day with a row the sorting could not read and a row
    // whose sender failed the checks.
    Marks,
}
