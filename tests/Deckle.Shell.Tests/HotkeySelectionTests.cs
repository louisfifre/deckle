using Deckle.Core;
using Deckle.Shell;
using Xunit;

namespace Deckle.Shell.Tests;

[Trait("Category", "unit")]
public sealed class HotkeySelectionTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 3)]
    public void ModulePresenceControlsTheExposedChords(
        bool transcriptionPresent,
        bool rewritePresent,
        int expectedCount)
    {
        IReadOnlyList<int> ids = HotkeySelection.ForModulePresence(
            transcriptionPresent,
            rewritePresent);

        Assert.Equal(expectedCount, ids.Count);
        Assert.Equal(
            transcriptionPresent,
            ids.Contains(NativeMethods.HOTKEY_ID_TRANSCRIBE));
        Assert.Equal(
            transcriptionPresent && rewritePresent,
            ids.Contains(NativeMethods.HOTKEY_ID_PRIMARY_REWRITE));
        Assert.Equal(
            transcriptionPresent && rewritePresent,
            ids.Contains(NativeMethods.HOTKEY_ID_SECONDARY_REWRITE));
    }
}
