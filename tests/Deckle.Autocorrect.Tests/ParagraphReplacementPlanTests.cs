using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// "Apply" on a rewritten paragraph erases the old one in another app (Word,
// a browser) with Backspaces, types the new one, and puts the Shift+Enter back.
// One Backspace too many eats the user's own text; one too few leaves a
// fragment of the old paragraph behind. The tests read the gesture as a user
// would see it: how many characters vanish, what gets typed, how the line ends.
[Trait("Category", "unit")]
public class ParagraphReplacementPlanTests
{
    // What the target app receives, in order, as plain facts.
    private sealed record Gesture(int Backspaces, string Typed, bool BackspacesComeFirst, bool EndsWithShiftEnter);

    private static Gesture Replay(string original, string replacement)
    {
        var keys = ParagraphReplacementPlan.Compute(original, replacement);

        int backspaces = keys.Count(k => k.VirtualKey == ParagraphReplacementPlan.VkBack && !k.Release);
        var typed = new string(keys.Where(k => k.IsUnicode && !k.Release).Select(k => (char)k.Unit).ToArray());

        int firstTyped = keys.ToList().FindIndex(k => k.IsUnicode);
        int lastBackspace = keys.ToList().FindLastIndex(k => k.VirtualKey == ParagraphReplacementPlan.VkBack);
        bool backspacesFirst = firstTyped < 0 || lastBackspace < firstTyped;

        var tail = keys.Skip(Math.Max(0, keys.Count - 4)).ToArray();
        bool shiftEnter = tail.Length == 4
            && tail[0] == new PlannedKey(ParagraphReplacementPlan.VkShift, 0, false)
            && tail[1] == new PlannedKey(ParagraphReplacementPlan.VkReturn, 0, false)
            && tail[2] == new PlannedKey(ParagraphReplacementPlan.VkReturn, 0, true)
            && tail[3] == new PlannedKey(ParagraphReplacementPlan.VkShift, 0, true);

        return new Gesture(backspaces, typed, backspacesFirst, shiftEnter);
    }

    [Fact]
    public void ErasesEachVisibleCharacterOfTheParagraphAndTheLineReturnAfterIt()
    {
        var gesture = Replay("bonjour", "Bonjour,");

        Assert.Equal("bonjour".Length + 1, gesture.Backspaces);
    }

    [Fact]
    public void AnEmptyParagraphOnlyErasesTheLineReturn()
    {
        var gesture = Replay("", "Bonjour");

        Assert.Equal(1, gesture.Backspaces);
        Assert.Equal("Bonjour", gesture.Typed);
    }

    [Fact]
    public void ALetterWithACombiningAccentIsOneBackspace()
    {
        // "e" followed by U+0301: two code points, one character on screen.
        var gesture = Replay("cafe\u0301", "café");

        Assert.Equal("café".Length + 1, gesture.Backspaces);
    }

    [Fact]
    public void AnEmojiMadeOfASurrogatePairIsOneBackspace()
    {
        var gesture = Replay("ok \U0001F44D", "d'accord");

        Assert.Equal("ok ".Length + 1 + 1, gesture.Backspaces);
    }

    [Fact]
    public void AnEmojiSequenceJoinedByZeroWidthJoinersIsOneBackspace()
    {
        // Man, woman, girl joined into one family emoji.
        var gesture = Replay("famille \U0001F468\u200D\U0001F469\u200D\U0001F467", "ma famille");

        Assert.Equal("famille ".Length + 1 + 1, gesture.Backspaces);
    }

    [Fact]
    public void TypesTheWholeReplacementAccentsAndEmojiIncluded()
    {
        const string replacement = "Où est l'été ? \U0001F44D";

        var gesture = Replay("ou est l'ete ?", replacement);

        Assert.Equal(replacement, gesture.Typed);
    }

    [Fact]
    public void ErasesFirstThenTypesTheReplacement()
    {
        var gesture = Replay("bonjour", "salut");

        Assert.True(gesture.BackspacesComeFirst);
    }

    [Theory]
    [InlineData("bonjour", "salut")]
    [InlineData("", "salut")]
    [InlineData("salut", "")]
    public void TheLineReturnIsRecreatedAsAShiftEnterAfterTheReplacement(string original, string replacement)
    {
        var gesture = Replay(original, replacement);

        Assert.True(gesture.EndsWithShiftEnter);
    }

    [Fact]
    public void AnEmptyReplacementStillErasesTheParagraphAndPutsTheReturnBack()
    {
        var gesture = Replay("bonjour", "");

        Assert.Equal("bonjour".Length + 1, gesture.Backspaces);
        Assert.Equal("", gesture.Typed);
        Assert.True(gesture.EndsWithShiftEnter);
    }

    [Fact]
    public void EveryKeyThatGoesDownComesBackUp()
    {
        var keys = ParagraphReplacementPlan.Compute("café 👍", "Café, d'accord");

        // Whatever the app sees pressed it also sees released, or a key stays stuck.
        var held = new HashSet<(ushort, ushort)>();
        foreach (var k in keys)
        {
            if (!k.Release) held.Add((k.VirtualKey, k.Unit));
            else Assert.True(held.Remove((k.VirtualKey, k.Unit)), "a key was released that was never pressed");
        }
        Assert.Empty(held);
    }
}
