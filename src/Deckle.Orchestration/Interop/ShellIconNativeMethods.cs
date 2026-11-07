using System.Runtime.InteropServices;

namespace Deckle.Orchestration;

// The shell calls that turn a file into its icon: SHCreateItemFromParsingName
// builds a shell item, and its IShellItemImageFactory renders it to a bitmap.
// The interface is called through its virtual table (the same choice as
// Deckle.Vision's interop), so no [ComImport] type shares this project with
// the WinRT projection.
internal static unsafe class ShellIconNativeMethods
{
    // IShellItemImageFactory: IUnknown (0 to 2), then GetImage.
    private static readonly Guid IidShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    private const int ReleaseSlot = 2;
    private const int GetImageSlot = 3;

    // Icon only: no thumbnail, which would read the file's content.
    private const int SIIGBF_ICONONLY = 0x4;

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
    private static extern int SHCreateItemFromParsingName(
        string path, nint bindContext, in Guid riid, out nint item);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int GetObjectW(nint handle, int count, out Bitmap bitmap);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int GetDIBits(
        nint hdc, nint hbm, uint start, uint lines, byte* bits, ref BitmapInfoHeader info, uint usage);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ReleaseDC(nint window, nint hdc);

    /// <summary>
    /// Render the file's icon at size x size pixels as top-down BGRA, or throw
    /// a COMException carrying the failing HRESULT.
    /// </summary>
    internal static ExecutableIcon GetIcon(string path, int size)
    {
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, 0, IidShellItemImageFactory, out nint factory));
        try
        {
            nint hbitmap;
            void** vtable = *(void***)factory;
            var getImage = (delegate* unmanaged[Stdcall]<nint, Size, int, nint*, int>)vtable[GetImageSlot];
            Marshal.ThrowExceptionForHR(getImage(factory, new Size { Cx = size, Cy = size }, SIIGBF_ICONONLY, &hbitmap));
            try
            {
                return ReadPixels(hbitmap);
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

    private static ExecutableIcon ReadPixels(nint hbitmap)
    {
        if (GetObjectW(hbitmap, sizeof(Bitmap), out Bitmap bitmap) == 0 || bitmap.Width <= 0 || bitmap.Height <= 0)
            throw new InvalidOperationException("The shell returned an unreadable bitmap.");

        var info = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = bitmap.Width,
            Height = -bitmap.Height, // negative: rows run top to bottom
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
                if (GetDIBits(hdc, hbitmap, 0, (uint)bitmap.Height, buffer, ref info, DIB_RGB_COLORS) == 0)
                    throw new InvalidOperationException("The bitmap's pixels could not be read.");
            }
        }
        finally
        {
            ReleaseDC(0, hdc);
        }

        return new ExecutableIcon(bitmap.Width, bitmap.Height, NormalizeAlpha(pixels));
    }

    // A bitmap without an alpha channel reads as fully transparent; give it
    // full opacity so it shows. One with alpha is left as the shell drew it
    // (premultiplied, which is what a WriteableBitmap expects).
    private static byte[] NormalizeAlpha(byte[] pixels)
    {
        bool anyColor = false;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] != 0) return pixels;
            if ((pixels[i] | pixels[i + 1] | pixels[i + 2]) != 0) anyColor = true;
        }

        if (anyColor)
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return pixels;
    }
}
