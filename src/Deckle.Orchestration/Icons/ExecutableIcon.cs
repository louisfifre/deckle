namespace Deckle.Orchestration;

/// <summary>
/// An executable's icon as raw pixels: top-down BGRA, premultiplied alpha. No
/// interface type, so the reader stays free of the UI; the page turns the
/// pixels into a bitmap.
/// </summary>
public sealed record ExecutableIcon(int Width, int Height, byte[] Bgra);
