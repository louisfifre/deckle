using Xunit;

namespace Deckle.Mail.Hub.Tests;

// The first mail the person sends to the trash asks first where treated mail
// goes (spec, The trash). Nothing reaches the source until the notice is
// accepted; once accepted it is not asked again in this window.
[Trait("Category", "unit")]
public sealed class TrashNoticeTests
{
    private static (TriageSession Session, SampleMailHubSource Source) Open()
    {
        var source = new SampleMailHubSource(SampleScenario.NormalDay);
        return (new TriageSession(source, new ManualClock()), source);
    }

    [Fact]
    public async Task NothingIsSentToTheTrashWhileTheFirstNoticeIsRefused()
    {
        (TriageSession session, SampleMailHubSource source) = Open();

        TrashOutcome outcome = await session.MoveToTrashAsync("s-drive", () => Task.FromResult(false));

        Assert.Equal(TrashOutcome.Declined, outcome);
        Assert.False(session.FindRow("s-drive")!.IsTreated);
        Assert.Empty(source.Applied);
        Assert.False(session.IsTrashNoticeAccepted);
    }

    [Fact]
    public async Task AcceptingTheFirstNoticeSendsTheMailToTheTrash()
    {
        (TriageSession session, SampleMailHubSource source) = Open();

        TrashOutcome outcome = await session.MoveToTrashAsync("s-drive", () => Task.FromResult(true));

        Assert.Equal(TrashOutcome.Applied, outcome);
        Assert.True(session.FindRow("s-drive")!.IsTreated);
        Assert.Equal(new MailGesture(MailGestureKind.MoveToTrash, "s-drive"), source.Applied.Single());
    }

    [Fact]
    public async Task TheNoticeIsAskedAgainAfterARefusalButNotAfterAnAcceptance()
    {
        (TriageSession session, _) = Open();
        int asked = 0;
        Task<bool> Ask(bool answer) { asked++; return Task.FromResult(answer); }

        await session.MoveToTrashAsync("s-drive", () => Ask(false));
        await session.MoveToTrashAsync("s-drive", () => Ask(true));
        await session.MoveToTrashAsync("s-camille", () => Ask(true));

        Assert.Equal(2, asked);
        Assert.True(session.FindRow("s-camille")!.IsTreated);
    }
}
