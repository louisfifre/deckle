using System.Runtime.InteropServices;

namespace Deckle.Atelier;

// The shell calls that draw a file: SHCreateItemFromParsingName builds a shell
// item, its IShellItemImageFactory draws it to an HBITMAP, and GDI copies the
// bitmap's pixels out. Hand-written, as the repository's other shell interop
// is (no CsWin32 here); the factory is called through its virtual table, the
// choice Deckle.Orchestration's ShellIconNativeMethods made for the same call,
// so no [ComImport] type is needed.
internal static unsafe class ShellImageNativeMethods
{
    // IShellItemImageFactory: IUnknown (slots 0 to 2), then GetImage.
    private static readonly Guid IidShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    private const int ReleaseSlot = 2;
    private const int GetImageSlot = 3;

    // SIIGBF, as IShellItemImageFactory::GetImage documents it (Microsoft Learn).
    internal const int SIIGBF_BIGGERSIZEOK = 0x1;
    internal const int SIIGBF_ICONONLY = 0x4;
    internal const int SIIGBF_THUMBNAILONLY = 0x8;
    internal const int SIIGBF_INCACHEONLY = 0x10;

    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, in Guid riid, out nint item);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int GetObjectW(nint handle, int count, out Bitmap bitmap);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int GetDIBits(nint hdc, nint hbm, uint start, uint lines, byte* bits, ref BitmapInfoHeader info, uint usage);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ReleaseDC(nint window, nint hdc);

    // The item drawn at size x size with the given SIIGBF flags, or the
    // failing HRESULT. The caller owns the HBITMAP GetImage returns and frees
    // it with DeleteObject (Microsoft Learn): done here, once the pixels are
    // copied, whatever happens.
    internal static int TryGetImage(string path, int size, int flags, out ShellImage? image)
    {
        image = null;
        int hr = SHCreateItemFromParsingName(path, 0, IidShellItemImageFactory, out nint factory);
        if (hr < 0) return hr;
        try
        {
            nint hbitmap;
            var getImage = (delegate* unmanaged[Stdcall]<nint, Size, int, nint*, int>)(*(void***)factory)[GetImageSlot];
            hr = getImage(factory, new Size { Cx = size, Cy = size }, flags, &hbitmap);
            if (hr < 0) return hr;
            try
            {
                image = ReadPixels(hbitmap);
                return image is null ? unchecked((int)0x80004005) /* E_FAIL */ : 0;
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }
        finally
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)factory)[ReleaseSlot])(factory);
        }
    }

    // GetDIBits with a negative height hands the rows top to bottom whatever
    // order the bitmap keeps them in (a DIB section is often bottom-up), as
    // 32-bit BGRA.
    private static ShellImage? ReadPixels(nint hbitmap)
    {
        if (GetObjectW(hbitmap, sizeof(Bitmap), out Bitmap bitmap) == 0 || bitmap.Width <= 0 || bitmap.Height <= 0)
            return null;

        var info = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = bitmap.Width,
            Height = -bitmap.Height,
            Planes = 1,
            BitCount = 32,
            Compression = BI_RGB,
        };
        var pixels = new byte[bitmap.Width * bitmap.Height * 4];
        nint hdc = GetDC(0);
        try
        {
            fixed (byte* buffer = pixels)
            {
                if (GetDIBits(hdc, hbitmap, 0, (uint)bitmap.Height, buffer, ref info, DIB_RGB_COLORS) == 0) return null;
            }
        }
        finally
        {
            ReleaseDC(0, hdc);
        }

        ShellImagePixels.OpaqueIfNoAlpha(pixels);
        return new ShellImage(bitmap.Width, bitmap.Height, pixels);
    }
}
