using System.Runtime.InteropServices;

namespace Deckle.Core;

// ─── Win32 clipboard write ────────────────────────────────────────────────────
//
// Deliberately the raw Win32 path (GlobalAlloc + SetClipboardData) rather than
// the WinRT Windows.ApplicationModel.DataTransfer.Clipboard / DataPackage API.
// A concrete global memory handle hands the bytes to the OS clipboard the moment
// SetClipboardData returns — no delayed rendering. The WinRT SetContent path, by
// contrast, keeps the data source-owned and served lazily until Clipboard.Flush
// is called, which is unreliable for large payloads and fails silently. Any
// proposal to route this back through the WinRT API must justify it against that.
//
// TryCopyText is pure: it performs the write and an immediate length read-back,
// and returns a structured result. It logs nothing and shows nothing — each
// caller maps the result to its own observability and user feedback.

public enum ClipboardWriteStatus
{
    // The bytes reached the OS clipboard and the read-back matched.
    Success,
    // GlobalAlloc/GlobalLock returned no usable handle — the text was never written.
    AllocFailed,
    // OpenClipboard was refused (another process holds the clipboard).
    OpenFailed,
    // SetClipboardData returned a null handle — the OS refused the payload.
    SetDataFailed,
    // The write succeeded but the read-back found no CF_UNICODETEXT data.
    VerifyMissing,
    // The write succeeded but the read-back length differs from what we wrote.
    VerifyLengthMismatch,
}

// Status carries the outcome; the numeric fields feed the caller's logs.
// ActualChars is -1 when the read-back could not run or found nothing.
public readonly record struct ClipboardWriteResult(
    ClipboardWriteStatus Status,
    int ExpectedChars,
    int ActualChars,
    int ByteCount,
    long Handle)
{
    // True when the bytes reached the OS clipboard — Success or either Verify
    // outcome. The Verify* states are advisory: the copy landed, but a
    // third-party clipboard watcher may have re-encoded or trimmed it between
    // the write and our read-back. Only the three hard failures return false.
    public bool Landed => Status is not (ClipboardWriteStatus.AllocFailed
                                      or ClipboardWriteStatus.OpenFailed
                                      or ClipboardWriteStatus.SetDataFailed);
}

public static class Win32Clipboard
{
    private const uint GMEM_MOVEABLE  = 0x0002;
    private const uint CF_UNICODETEXT = 13;

    // P/Invokes co-located here because this writer is their only consumer.
    // Keeping them private keeps NativeMethods free of clipboard plumbing and
    // makes the capability self-contained.
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();
    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    public static ClipboardWriteResult TryCopyText(string text)
    {
        int expected  = text.Length;
        int byteCount = (expected + 1) * 2; // UTF-16 + null terminator.

        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
        if (hMem == IntPtr.Zero)
            return new ClipboardWriteResult(ClipboardWriteStatus.AllocFailed, expected, -1, byteCount, 0);

        IntPtr ptr = GlobalLock(hMem);
        if (ptr == IntPtr.Zero)
            // The handle exists but can't be locked — treat as a memory failure.
            // The handle leaks here, consistent with the other failure paths.
            return new ClipboardWriteResult(ClipboardWriteStatus.AllocFailed, expected, -1, byteCount, (long)hMem);

        Marshal.Copy(text.ToCharArray(), 0, ptr, expected);
        Marshal.WriteInt16(ptr, expected * 2, 0);
        GlobalUnlock(hMem);

        if (!OpenClipboard(IntPtr.Zero))
            return new ClipboardWriteResult(ClipboardWriteStatus.OpenFailed, expected, -1, byteCount, (long)hMem);

        EmptyClipboard();
        IntPtr setHandle = SetClipboardData(CF_UNICODETEXT, hMem);
        CloseClipboard();
        if (setHandle == IntPtr.Zero)
            return new ClipboardWriteResult(ClipboardWriteStatus.SetDataFailed, expected, -1, byteCount, (long)hMem);

        // Immediate read-back to verify the length. Best-effort: if the clipboard
        // can't be reopened we leave the write as Success (the bytes were set).
        ClipboardWriteStatus status = ClipboardWriteStatus.Success;
        int actual = -1;
        if (OpenClipboard(IntPtr.Zero))
        {
            IntPtr h = GetClipboardData(CF_UNICODETEXT);
            if (h == IntPtr.Zero)
            {
                status = ClipboardWriteStatus.VerifyMissing;
            }
            else
            {
                IntPtr p = GlobalLock(h);
                string? back = p != IntPtr.Zero ? Marshal.PtrToStringUni(p) : null;
                GlobalUnlock(h);
                actual = back?.Length ?? -1;
                if (back is null || back.Length != expected)
                    status = ClipboardWriteStatus.VerifyLengthMismatch;
            }
            CloseClipboard();
        }

        return new ClipboardWriteResult(status, expected, actual, byteCount, (long)hMem);
    }
}
