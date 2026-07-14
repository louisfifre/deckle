using Deckle.Core;
using Deckle.Shell;
using Xunit;

namespace Deckle.Shell.Tests;

[Trait("Category", "unit")]
public sealed class HotkeySelectionTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public void RewritePresenceControlsTheExposedChords(bool present, int expectedCount)
    {
        IReadOnlyList<int> ids = HotkeySelection.ForRewritePresence(present);

        Assert.Equal(expectedCount, ids.Count);
        Assert.Contains(NativeMethods.HOTKEY_ID_TRANSCRIBE, ids);
        Assert.Equal(present, ids.Contains(NativeMethods.HOTKEY_ID_PRIMARY_REWRITE));
        Assert.Equal(present, ids.Contains(NativeMethods.HOTKEY_ID_SECONDARY_REWRITE));
    }
}
