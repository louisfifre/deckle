namespace Deckle.Core;

// The icon a person gives a place of their own: an added folder in the
// Folders, later a mailbox in Mail (specs/folders/deckle-folders.md; specs/mail/
// deckle-mail.md). A symbol from a small fixed set, a tint, and
// whether the tint fills a square behind the symbol. Without the background
// the symbol itself takes the tint; there are no shapes to choose.
//
// It lives here, beside HotkeyChord, for the same reason: engines that hold
// no XAML persist it, and a control of Deckle.Catalog edits it (IconPicker),
// so the type sits in the one module both already reference.
//
// default(IconChoice) is the plain folder in the accent colour: a choice read
// from a file written before icons existed takes it.
public readonly record struct IconChoice(IconSymbol Symbol, IconTint Tint, bool HasBackground)
{
    public static IconChoice Default => default;
}
