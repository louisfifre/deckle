using Deckle.Core;
using Deckle.Shell;
using Xunit;

namespace Deckle.Shell.Tests;

[Trait("Category", "unit")]
public sealed class HotkeySelectionTests
{
    [Fact]
    public void TranscriptionPresenceExposesOnlyTheTranscribeChord()
    {
        IReadOnlyList<int> ids = HotkeySelection.ForModulePresence(transcriptionPresent: true);

        Assert.Equal([NativeMethods.HOTKEY_ID_TRANSCRIBE], ids);
    }

    [Fact]
    public void AbsentTranscriptionExposesNoChord()
    {
        IReadOnlyList<int> ids = HotkeySelection.ForModulePresence(transcriptionPresent: false);

        Assert.Empty(ids);
    }
}
