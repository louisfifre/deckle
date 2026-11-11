namespace Deckle.Atelier;

// The one correction the shell's pixels need before WinUI shows them.
//
// A 32-bit bitmap with an alpha channel comes premultiplied: the shell draws
// it for AlphaBlend, which takes premultiplied pixels, and a WriteableBitmap
// takes them so as well, so those pass through untouched. A bitmap drawn
// without alpha (an opaque thumbnail, an old 24-bit icon) leaves every alpha
// byte at zero, which a WriteableBitmap would show as fully transparent: such
// a bitmap is made opaque. An all-zero bitmap stays as it is, transparent.
public static class ShellImagePixels
{
    public static void OpaqueIfNoAlpha(Span<byte> bgra)
    {
        bool anyColor = false;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] != 0) return;
            if ((bgra[i] | bgra[i + 1] | bgra[i + 2]) != 0) anyColor = true;
        }
        if (!anyColor) return;
        for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
    }
}
