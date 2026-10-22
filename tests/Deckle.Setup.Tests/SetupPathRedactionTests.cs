using Deckle.TestSupport;
using Xunit;

namespace Deckle.Setup.Tests;

// The install location the person confirmed is a folder, often in the profile.
[Trait("Category", "observability")]
public sealed class SetupPathRedactionTests
{
    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void TheConfirmedLocationNamesTheFolderUnderTheProfileVariable()
    {
        using var listener = new TestEventListener("Deckle-Setup");

        DeckleSetupSource.Log.ChoicesConfirmedDetail(Path.Combine(Profile, "Deckle"), "base");

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleSetupSource.EvtChoicesConfirmedDetail);
        Assert.Equal(@"%USERPROFILE%\Deckle", ev.Payload![0]);
    }
}
