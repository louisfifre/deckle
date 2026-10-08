using System.Text.Json;
using Deckle.Catalog;
using Xunit;

namespace Deckle.Catalog.Tests;

// The chord is persisted in settings.json (camelCase, like every Deckle
// store) and gates registration through IsComplete.
[Trait("Category", "unit")]
public class HotkeyChordTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void ChordSerializesAsModifiersAndScanCode()
    {
        var chord = new HotkeyChord(HotkeyModifiers.Win, 0x29);

        string json = JsonSerializer.Serialize(chord, CamelCase);

        Assert.Equal("""{"modifiers":8,"scanCode":41}""", json);
    }

    [Fact]
    public void ChordRoundTripsThroughJson()
    {
        var chord = new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0xE04B);

        HotkeyChord? back = JsonSerializer.Deserialize<HotkeyChord?>(
            JsonSerializer.Serialize<HotkeyChord?>(chord, CamelCase), CamelCase);

        Assert.Equal(chord, back);
    }

    [Fact]
    public void UnboundChordRoundTripsAsNull()
    {
        HotkeyChord? unbound = null;

        string json = JsonSerializer.Serialize(unbound, CamelCase);

        Assert.Equal("null", json);
        Assert.Null(JsonSerializer.Deserialize<HotkeyChord?>(json, CamelCase));
    }

    [Theory]
    [InlineData(HotkeyModifiers.Win, 0x29u, true)]
    [InlineData(HotkeyModifiers.None, 0x29u, false)]
    [InlineData(HotkeyModifiers.Alt, 0u, false)]
    [InlineData(HotkeyModifiers.None, 0u, false)]
    public void ChordIsCompleteOnlyWithAModifierAndAKey(
        HotkeyModifiers modifiers, uint scanCode, bool expected)
    {
        Assert.Equal(expected, new HotkeyChord(modifiers, scanCode).IsComplete);
    }

    [Fact]
    public void ModifierBitsMatchTheWin32Flags()
    {
        // MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_WIN: the shell casts, so these
        // values are the contract.
        Assert.Equal(0x1, (int)HotkeyModifiers.Alt);
        Assert.Equal(0x2, (int)HotkeyModifiers.Control);
        Assert.Equal(0x4, (int)HotkeyModifiers.Shift);
        Assert.Equal(0x8, (int)HotkeyModifiers.Win);
    }
}
