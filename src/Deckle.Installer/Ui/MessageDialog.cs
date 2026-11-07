using System.Runtime.InteropServices;

namespace Deckle.Installer;

// The stub's only other visible surface: a native MessageBox, for the terminal
// errors the progress window can't express and for the two uninstall confirmations.
// A plain MessageBoxW is enough — a TaskDialog would drag comctl32 activation-context
// setup for no gain here. Source-generated interop, UTF-16, AOT-safe.
internal static partial class MessageDialog
{
    public static void Error(nint owner, string text) =>
        MessageBoxW(owner, text, "Deckle Setup", MB_OK | MB_ICONERROR);

    public static bool Confirm(nint owner, string text, string title) =>
        MessageBoxW(owner, text, title, MB_YESNO | MB_ICONQUESTION) == IDYES;

    // For a deletion that cannot be taken back: Enter and Esc land on No, so
    // the person has to choose Yes on purpose.
    public static bool ConfirmDestructive(nint owner, string text, string title) =>
        MessageBoxW(owner, text, title, MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2) == IDYES;

    private const uint MB_OK = 0x0;
    private const uint MB_YESNO = 0x4;
    private const uint MB_ICONERROR = 0x10;
    private const uint MB_ICONQUESTION = 0x20;
    private const uint MB_ICONWARNING = 0x30;
    private const uint MB_DEFBUTTON2 = 0x100;
    private const int IDYES = 6;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint hWnd, string lpText, string lpCaption, uint uType);
}
