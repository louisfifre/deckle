using System.Diagnostics;
using Xunit;

namespace Deckle.Core.Tests;

// The paste events carry this description into the application log, so it
// names the process and the focused control's class, never the window
// title: a title names what the person is working on.
[Trait("Category", "regression")]
public sealed class Win32UtilTests
{
    [Fact]
    public void TheDescriptionNeverCarriesTheWindowTitle()
    {
        const string title = "Private draft.txt";

        // A built-in class needs no registration, and a popup that is never
        // shown stays off screen, out of the taskbar and out of Alt+Tab.
        IntPtr hwnd = NativeMethods.CreateWindowEx(
            0, "STATIC", title, NativeMethods.WS_POPUP,
            0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, NativeMethods.GetModuleHandle(null), IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, hwnd);

        try
        {
            string description = Win32Util.DescribeHwnd(hwnd);

            Assert.DoesNotContain(title, description);
            Assert.StartsWith(Process.GetCurrentProcess().ProcessName, description);
        }
        finally
        {
            NativeMethods.DestroyWindow(hwnd);
        }
    }
}
