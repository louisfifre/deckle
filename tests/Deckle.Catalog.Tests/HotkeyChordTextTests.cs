using Deckle.Catalog;
using Xunit;

namespace Deckle.Catalog.Tests;

// The one-line form of a chord. The key name comes from the machine's layout,
// so the tests pass a fixed resolver and check only the order and the gaps.
[Trait("Category", "unit")]
public class HotkeyChordTextTests
{
    private static string KeyName(uint scanCode) => $"<{scanCode:X}>";

    [Fact]
    public void ModifiersReadCtrlShiftAltWinThenTheKey()
    {
        var chord = new HotkeyChord(
            HotkeyModifiers.Win | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Control,
            0x29);

        Assert.Equal("Ctrl + Shift + Alt + Win + <29>", HotkeyChordText.Format(chord, KeyName));
    }

    [Fact]
    public void OnlyTheModifiersPresentAreWritten()
    {
        var chord = new HotkeyChord(HotkeyModifiers.Win | HotkeyModifiers.Shift, 0xE04B);

        Assert.Equal("Shift + Win + <E04B>", HotkeyChordText.Format(chord, KeyName));
    }

    [Fact]
    public void AnUnboundChordHasNoText()
    {
        Assert.Null(HotkeyChordText.Format(null, KeyName));
    }
}
