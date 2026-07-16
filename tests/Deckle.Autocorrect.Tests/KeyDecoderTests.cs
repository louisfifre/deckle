using System.Text;
using Deckle.Autocorrect;
using Deckle.Input;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Tests de comportement sur KeyDecoder — la traduction du flux clavier brut en
// Keystroke. On exerce deux choses : la tenue de l'état modificateur depuis le
// stream (le thread hôte n'est pas le thread focus, donc GetKeyboardState
// mentirait) et la classification d'une touche enfoncée. ToUnicodeEx est
// injecté : un faux qui regarde l'état Shift/AltGr suffit à prouver le câblage,
// sans dépendre d'une disposition vivante.
[Trait("Category", "unit")]
public class KeyDecoderTests
{
    private const ushort VkA = 0x41;       // 'A'
    private const ushort VkE = 0x45;       // 'E'
    private const ushort VkShift = 0x10;
    private const ushort VkLShift = 0xA0;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;    // Alt
    private const ushort VkLWin = 0x5B;
    private const ushort VkCapital = 0x14;
    private const ushort VkBack = 0x08;
    private const ushort VkLeft = 0x25;

    private const byte Down = 0x80;
    private const byte Toggle = 0x01;

    private static KeyboardKeyEvent Key(ushort vk, bool down, double t = 0) =>
        new(vk, ScanCode: 0, IsKeyDown: down, IsExtended: false, IsInjected: false, TimestampMs: t);

    // Faux ToUnicodeEx : 'A'→"a", capitalisé si Shift OU Caps actif dans l'état ;
    // 'E' en chord AltGr (Ctrl+Alt dans l'état) → "€". Sinon "x".
    private static KeyDecoder DecoderWithFakeLayout(bool capsToggled = false)
    {
        return new KeyDecoder((vk, _, state, buf) =>
        {
            bool shift = state[VkShift] == Down;
            bool caps = state[VkCapital] == Toggle;
            bool altGr = state[VkControl] == Down && state[VkMenu] == Down;

            buf.Clear();
            if (vk == VkA)
            {
                bool upper = shift ^ caps; // caps inverse l'effet de Shift sur une lettre
                buf.Append(upper ? 'A' : 'a');
                return 1;
            }
            if (vk == VkE)
            {
                buf.Append(altGr ? '€' : (shift ^ caps ? 'E' : 'e'));
                return 1;
            }
            buf.Append('x');
            return 1;
        }, capsToggled);
    }

    [Fact]
    public void ModifierKeysProduceNoOutputOnDownOrUp()
    {
        var d = DecoderWithFakeLayout();
        Assert.Null(d.Decode(Key(VkShift, down: true)));
        Assert.Null(d.Decode(Key(VkShift, down: false)));
        Assert.Null(d.Decode(Key(VkControl, down: true)));
        Assert.Null(d.Decode(Key(VkLWin, down: false)));
    }

    [Fact]
    public void NonModifierKeyUpProducesNoOutput()
    {
        var d = DecoderWithFakeLayout();
        Assert.Null(d.Decode(Key(VkA, down: false)));
    }

    [Fact]
    public void PlainLetterDecodesToLowercaseText()
    {
        var d = DecoderWithFakeLayout();
        var k = d.Decode(Key(VkA, down: true));
        Assert.NotNull(k);
        Assert.Equal(KeystrokeKind.Text, k!.Value.Kind);
        Assert.Equal("a", k.Value.Text);
    }

    [Fact]
    public void ShiftHeldCapitalizesThenReleaseRestores()
    {
        var d = DecoderWithFakeLayout();

        d.Decode(Key(VkLShift, down: true)); // sided shift down
        var upper = d.Decode(Key(VkA, down: true));
        Assert.Equal("A", upper!.Value.Text);

        d.Decode(Key(VkLShift, down: false));
        var lower = d.Decode(Key(VkA, down: true));
        Assert.Equal("a", lower!.Value.Text);
    }

    [Fact]
    public void CapsLockTogglesOnDownAndSeedsFromConstruction()
    {
        // Démarre avec Caps déjà actif (semé depuis GetKeyState à la construction).
        var d = DecoderWithFakeLayout(capsToggled: true);
        Assert.True(d.CapsToggled);
        Assert.Equal("A", d.Decode(Key(VkA, down: true))!.Value.Text);

        // Un appui VK_CAPITAL bascule l'état (le up ne fait rien).
        d.Decode(Key(VkCapital, down: true));
        Assert.False(d.CapsToggled);
        d.Decode(Key(VkCapital, down: false));
        Assert.False(d.CapsToggled);
        Assert.Equal("a", d.Decode(Key(VkA, down: true))!.Value.Text);
    }

    [Fact]
    public void CtrlChordIsShortcutNotText()
    {
        var d = DecoderWithFakeLayout();
        d.Decode(Key(VkControl, down: true));
        var k = d.Decode(Key(VkA, down: true)); // Ctrl+A
        Assert.Equal(KeystrokeKind.Shortcut, k!.Value.Kind);
    }

    [Fact]
    public void AltGrChordStillDecodesToCharacter()
    {
        var d = DecoderWithFakeLayout();
        d.Decode(Key(VkControl, down: true));
        d.Decode(Key(VkMenu, down: true)); // Ctrl+Alt = AltGr
        var k = d.Decode(Key(VkE, down: true));
        Assert.Equal(KeystrokeKind.Text, k!.Value.Kind);
        Assert.Equal("€", k.Value.Text);
    }

    [Fact]
    public void ReleasedAltGrDoesNotLeakIntoTheNextTranslation()
    {
        var d = DecoderWithFakeLayout();
        d.Decode(Key(VkControl, down: true));
        d.Decode(Key(VkMenu, down: true));
        Assert.Equal("€", d.Decode(Key(VkE, down: true))!.Value.Text);

        d.Decode(Key(VkControl, down: false));
        d.Decode(Key(VkMenu, down: false));

        Assert.Equal("e", d.Decode(Key(VkE, down: true))!.Value.Text);
    }

    [Fact]
    public void WinChordIsShortcut()
    {
        var d = DecoderWithFakeLayout();
        d.Decode(Key(VkLWin, down: true));
        var k = d.Decode(Key(VkA, down: true));
        Assert.Equal(KeystrokeKind.Shortcut, k!.Value.Kind);
    }

    [Fact]
    public void LoneAltIsShortcut()
    {
        var d = DecoderWithFakeLayout();
        d.Decode(Key(VkMenu, down: true));
        var k = d.Decode(Key(VkA, down: true));
        Assert.Equal(KeystrokeKind.Shortcut, k!.Value.Kind);
    }

    [Fact]
    public void EditingAndNavigationKeysClassifyBeforeTranslation()
    {
        var d = DecoderWithFakeLayout();
        Assert.Equal(KeystrokeKind.Backspace, d.Decode(Key(VkBack, down: true))!.Value.Kind);
        Assert.Equal(KeystrokeKind.Navigation, d.Decode(Key(VkLeft, down: true))!.Value.Kind);
        Assert.Equal(KeystrokeKind.Enter, d.Decode(Key(0x0D, down: true))!.Value.Kind);
        Assert.Equal(KeystrokeKind.Tab, d.Decode(Key(0x09, down: true))!.Value.Kind);
        Assert.Equal(KeystrokeKind.Escape, d.Decode(Key(0x1B, down: true))!.Value.Kind);
        Assert.Equal(KeystrokeKind.Delete, d.Decode(Key(0x2E, down: true))!.Value.Kind);
    }

    [Fact]
    public void CtrlChordedEditingKeysAreShortcuts()
    {
        // Ctrl+Backspace supprime un mot entier à l'écran : le modéliser comme
        // un Backspace simple (un caractère) désynchroniserait tracker et écran
        // — et, revert armé, déclencherait une injection sous un Ctrl
        // physiquement tenu, que l'application relirait en suppressions de mots.
        var d = DecoderWithFakeLayout();
        d.Decode(Key(VkControl, down: true));
        Assert.Equal(KeystrokeKind.Shortcut, d.Decode(Key(VkBack, down: true))!.Value.Kind);
        Assert.Equal(KeystrokeKind.Shortcut, d.Decode(Key(0x2E, down: true))!.Value.Kind); // Ctrl+Delete
        Assert.Equal(KeystrokeKind.Shortcut, d.Decode(Key(0x0D, down: true))!.Value.Kind); // Ctrl+Enter
        Assert.Equal(KeystrokeKind.Shortcut, d.Decode(Key(VkLeft, down: true))!.Value.Kind); // Ctrl+Left

        // Relâché, les touches d'édition retrouvent leur classification simple.
        d.Decode(Key(VkControl, down: false));
        Assert.Equal(KeystrokeKind.Backspace, d.Decode(Key(VkBack, down: true))!.Value.Kind);
    }

    [Fact]
    public void WinChordedEditingKeyIsShortcut()
    {
        var d = DecoderWithFakeLayout();
        d.Decode(Key(VkLWin, down: true));
        Assert.Equal(KeystrokeKind.Shortcut, d.Decode(Key(VkBack, down: true))!.Value.Kind);
    }

    [Fact]
    public void ToUnicodeZeroIsOtherAndMinusOneIsDeadKey()
    {
        var dOther = new KeyDecoder((_, _, _, _) => 0);
        Assert.Equal(KeystrokeKind.Other, dOther.Decode(Key(VkA, down: true))!.Value.Kind);

        var dDead = new KeyDecoder((_, _, _, buf) => { buf.Append('^'); return -1; });
        Assert.Equal(KeystrokeKind.DeadKey, dDead.Decode(Key(VkA, down: true))!.Value.Kind);
    }

    [Fact]
    public void TimestampPassesThroughToKeystroke()
    {
        var d = DecoderWithFakeLayout();
        var k = d.Decode(Key(VkA, down: true, t: 1234.5));
        Assert.Equal(1234.5, k!.Value.TimestampMs);
    }
}
