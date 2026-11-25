using Xunit;

namespace Deckle.Projects.Tests;

[Trait("Category", "unit")]
public sealed class ScriptMenuEntryTests
{
    private static readonly ScriptMenuEntry Entry = new("build-run.0", "Build", "build-run.ps1", "", ["Debug"]);

    [Fact]
    public void AnEntryGivesAPinOfItsActionAndItsScript()
    {
        Assert.True(Entry.GivesPin(new PinnedAction(new ActionRef("build-run.0", "Debug"), "Build", "build-run.ps1")));
    }

    [Fact]
    public void AnEntryDoesNotGiveAPinWhoseIdentifierNowNamesAnotherScript()
    {
        Assert.False(Entry.GivesPin(new PinnedAction(new ActionRef("build-run.0", "Debug"), "Build", "other.ps1")));
    }

    [Fact]
    public void AnEntryDoesNotGiveAPinOfAnotherIdentifier()
    {
        Assert.False(Entry.GivesPin(new PinnedAction(new ActionRef("other.0"), "Build", "build-run.ps1")));
    }
}
