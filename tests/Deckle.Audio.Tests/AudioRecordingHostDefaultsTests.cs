using Xunit;

namespace Deckle.Audio.Tests;

// A host written before a member existed keeps its behaviour: the members
// added with a default are the ones a dictation host never declares.
public sealed class AudioRecordingHostDefaultsTests
{
    [Fact]
    [Trait("Category", "unit")]
    public void AHostThatDeclaresOnlyTheOriginalMembersIsADictationThatKeepsItsAudio()
    {
        IAudioRecordingHost host = new OriginalMembersHost();

        Assert.Equal("dictation", host.RecordingPurpose);
        Assert.True(host.RetainsAudio);
    }

    private sealed class OriginalMembersHost : IAudioRecordingHost
    {
        public int AudioInputDeviceId => -1;
        public int MaxRecordingDurationSeconds => 0;
        public bool MicrophoneTelemetryEnabled => false;
    }
}
