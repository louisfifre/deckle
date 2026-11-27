using System.Globalization;

namespace Deckle.Autocorrect;

// One key event of a replacement gesture, before it becomes a SendInput record.
// VirtualKey 0 means a Unicode character: Unit carries its UTF-16 code unit.
internal readonly record struct PlannedKey(ushort VirtualKey, ushort Unit, bool Release)
{
    public bool IsUnicode => VirtualKey == 0;
}

// The key gesture that replaces the paragraph sitting just before a Shift+Enter
// line return: erase the paragraph and the return, type the replacement, then
// recreate the return as a real Shift+Enter. The caret must still be directly
// after that return when the gesture runs.
//
// One Backspace per visible character (a text element, so a letter plus its
// combining accent or an emoji sequence counts once), plus one for the return.
// Counting UTF-16 code units or code points here would erase text the user
// wants to keep.
internal static class ParagraphReplacementPlan
{
    public const ushort VkBack = 0x08;
    public const ushort VkReturn = 0x0D;
    public const ushort VkShift = 0x10;

    public static IReadOnlyList<PlannedKey> Compute(string original, string replacement)
    {
        int backspaces = StringInfo.ParseCombiningCharacters(original).Length + 1;
        var keys = new List<PlannedKey>((backspaces + replacement.Length) * 2 + 4);

        for (int b = 0; b < backspaces; b++)
        {
            keys.Add(new PlannedKey(VkBack, 0, false));
            keys.Add(new PlannedKey(VkBack, 0, true));
        }

        foreach (char unit in replacement)
        {
            keys.Add(new PlannedKey(0, unit, false));
            keys.Add(new PlannedKey(0, unit, true));
        }

        keys.Add(new PlannedKey(VkShift, 0, false));
        keys.Add(new PlannedKey(VkReturn, 0, false));
        keys.Add(new PlannedKey(VkReturn, 0, true));
        keys.Add(new PlannedKey(VkShift, 0, true));
        return keys;
    }
}
