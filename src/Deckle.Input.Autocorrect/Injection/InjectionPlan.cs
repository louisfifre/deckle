namespace Deckle.Input.Autocorrect;

// The minimal keystroke diff that turns `current` (the text sitting left of
// the caret) into `target`: delete the divergent tail, then type the new one.
// Backspaces is a count of *keypresses*, not UTF-16 code units — one Backspace
// deletes one code point in most edit controls. Text is the suffix to inject.
public readonly record struct InjectionPlan(int Backspaces, string Text)
{
    public bool IsNoOp => Backspaces == 0 && Text.Length == 0;

    // Compares by Unicode code point so a surrogate pair (e.g. an emoji, a rare
    // CJK ext char) is never cut in half: the common prefix always ends on a
    // code-point boundary. Backspaces counts the *code points* of current past
    // that prefix — the conservative correct count for French, which is all-BMP
    // precomposed NFC (one code point ⇒ one Backspace). The known limitation:
    // an edit control that deletes a UTF-16 code unit per Backspace would
    // undercount on astral planes — irrelevant to the French diacritics target,
    // documented rather than guarded.
    public static InjectionPlan Compute(string current, string target)
    {
        int prefixUtf16 = 0;   // shared length in UTF-16 code units
        while (prefixUtf16 < current.Length && prefixUtf16 < target.Length)
        {
            int c = char.ConvertToUtf32(current, prefixUtf16);
            int t = char.ConvertToUtf32(target, prefixUtf16);
            if (c != t) break;
            prefixUtf16 += char.IsHighSurrogate(current[prefixUtf16]) ? 2 : 1;
        }

        // Count code points (not code units) in current's divergent tail.
        int backspaces = 0;
        for (int i = prefixUtf16; i < current.Length; i += char.IsHighSurrogate(current[i]) ? 2 : 1)
            backspaces++;

        return new InjectionPlan(backspaces, target[prefixUtf16..]);
    }
}
