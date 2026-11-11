namespace Deckle.Atelier;

// A picture the shell drew for a file, an icon or a thumbnail: Width x Height
// pixels, rows top to bottom, four bytes per pixel in B, G, R, A order with
// premultiplied alpha, the layout a WinUI WriteableBitmap takes as it is.
public sealed record ShellImage(int Width, int Height, byte[] Bgra)
{
    // What the picture weighs in memory, the unit the caches budget in.
    public long Bytes => Bgra.LongLength;
}
