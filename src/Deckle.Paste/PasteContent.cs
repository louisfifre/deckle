namespace Deckle.Paste;

// What a paste holds, already read from the clipboard by the window. The
// window picks one by priority (files, then an image, then text); the saver
// only writes what it is given.
public abstract record PasteContent;

// Text, written as a UTF-8 .txt file.
public sealed record PastedText(string Text) : PasteContent;

// An image the window has already encoded as PNG; the bytes are written as given.
public sealed record PastedImage(ReadOnlyMemory<byte> PngBytes) : PasteContent;

// Files or folders copied in Explorer, by their full paths; each is copied
// into the chosen folder under its own name.
public sealed record PastedFiles(IReadOnlyList<string> Paths) : PasteContent;
