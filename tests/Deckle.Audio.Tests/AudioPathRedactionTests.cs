using Deckle.TestSupport;
using Xunit;

namespace Deckle.Audio.Tests;

// The settings load line quotes the file it read, which sits in the profile.
[Collection("Audio operational observability")]
[Trait("Category", "observability")]
public sealed class AudioPathRedactionTests
{
    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void TheSettingsLoadLineNamesTheFileUnderTheProfileVariable()
    {
        using var listener = new TestEventListener("Deckle-Audio");

        DeckleAudioSource.Log.SettingsLoadComplete($"loaded {Profile}\\Deckle\\audio.json");

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleAudioSource.EvtSettingsLoadComplete);
        Assert.Equal(@"loaded %USERPROFILE%\Deckle\audio.json", ev.Payload![0]);
    }
}
