using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Deckle.App.SelfCheck;

// The icon the taskbar is given, as an image. A self-check window is shown off
// every monitor with no taskbar button (OffscreenWindow), so the button itself
// cannot be photographed; what the taskbar draws on it can. Two sources feed a
// button: the window's own big icon (what AppWindow.SetIcon sets, read back
// with WM_GETICON) and the icon embedded in Deckle.exe (the group, a pinned
// button, the Start shortcut). Each is written as a PNG with its alpha, or
// reported missing: Windows' generic icon is never drawn in its place.
internal static class IconImage
{
    private const uint WM_GETICON = 0x007F;
    private const nint ICON_BIG = 1;
    private const int GCLP_HICON = -14;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    // The window's big icon, the class icon when it has none. Null when the
    // window carries no icon of its own.
    public static nint WindowIcon(nint hwnd)
    {
        if (SendMessageTimeout(hwnd, WM_GETICON, ICON_BIG, 0, SMTO_ABORTIFHUNG, 1000, out nint icon) != 0 && icon != 0)
            return icon;
        return GetClassLongPtr(hwnd, GCLP_HICON);
    }

    // The exe's first icon at `size` pixels, the frame the shell would pick.
    // The caller owns it (DestroyIcon). Null when the exe has none.
    public static nint ExeIcon(string exePath, int size)
    {
        int hr = SHDefExtractIcon(exePath, 0, 0, out nint large, out nint small, (uint)size | (16u << 16));
        if (small != 0) DestroyIcon(small);
        return hr == 0 ? large : 0;
    }

    public static void Destroy(nint icon)
    {
        if (icon != 0) DestroyIcon(icon);
    }

    // Writes the icon's colour bitmap, straight alpha, to `path`. Returns its
    // side in pixels.
    public static async Task<int> SaveAsync(nint icon, string path)
    {
        if (!GetIconInfo(icon, out ICONINFO info))
            throw new InvalidOperationException($"GetIconInfo failed ({Marshal.GetLastWin32Error()}).");
        try
        {
            if (info.hbmColor == 0) throw new InvalidOperationException("The icon is monochrome.");
            if (GetObject(info.hbmColor, Marshal.SizeOf<BITMAP>(), out BITMAP bitmap) == 0)
                throw new InvalidOperationException("The icon's bitmap could not be read.");
            int width = bitmap.bmWidth, height = bitmap.bmHeight;

            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            byte[] bgra = new byte[width * height * 4];
            nint dc = GetDC(0);
            try
            {
                if (GetDIBits(dc, info.hbmColor, 0, (uint)height, bgra, ref header, 0) == 0)
                    throw new InvalidOperationException("The icon's pixels could not be read.");
            }
            finally { ReleaseDC(0, dc); }

            using var stream = new InMemoryRandomAccessStream();
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                (uint)width, (uint)height, 96, 96, bgra);
            await encoder.FlushAsync();
            stream.Seek(0);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (FileStream file = File.Create(path))
                await stream.AsStreamForRead().CopyToAsync(file);
            return width;
        }
        finally
        {
            if (info.hbmColor != 0) DeleteObject(info.hbmColor);
            if (info.hbmMask != 0) DeleteObject(info.hbmMask);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public nint hbmMask;
        public nint hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern nint GetClassLongPtr(nint hwnd, int index);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string iconFile, int index, uint flags, out nint large, out nint small, uint iconSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(nint icon, out ICONINFO info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(nint handle, int size, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint dc);
}
