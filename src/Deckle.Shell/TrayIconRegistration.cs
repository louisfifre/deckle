using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Shell;

// The Shell32 call behind the notification area, one method for the three
// messages (NIM_ADD, NIM_MODIFY, NIM_DELETE) so a test can stand in for the
// shell and record what it was sent.
internal interface INotifyIconApi
{
    bool Notify(uint message, ref NOTIFYICONDATA data);
    int LastError { get; }
}

// What one re-add attempt concluded. RetryLater asks the caller to come back
// after a pause; GaveUp closes the sequence after MaxReaddAttempts.
internal enum TrayIconReaddOutcome { Added, RetryLater, GaveUp }

// The icon's entry in the notification area as the shell holds it: one
// (window, id) pair, an icon, a tip and the callback message. The shell
// starts from an empty table each time Explorer restarts, so this class
// keeps the last icon and tip it sent and can send the whole entry again.
//
// It knows no Loc and no Win32 beyond INotifyIconApi: the manager formats
// the tip and loads the icons, a test passes a fake shell.
internal sealed class TrayIconRegistration(IntPtr window, INotifyIconApi api)
{
    public const uint IconId = 1;

    // Tip cap: szTip holds 128 WCHARs, terminator included.
    public const int MaxTipLength = 127;

    // The shell refuses an add while its tray window is still coming up; a
    // handful of spaced attempts covers an ordinary Explorer restart.
    public const int MaxReaddAttempts = 5;

    private string _tip = "";
    private IntPtr _icon;
    private bool _added;
    private int _readdAttempts;

    public IntPtr Window => window;
    public bool IsAdded => _added;
    public string Tip => _tip;
    public IntPtr Icon => _icon;

    // Attempts made in the current re-add sequence, for the journal.
    public int ReaddAttempts => _readdAttempts;

    // First add, with the status kept so far: Update may have run before.
    public bool Add()
    {
        var data = BuildData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP);
        _added = api.Notify(NativeMethods.NIM_ADD, ref data);
        return _added;
    }

    // Keeps the status whether or not the icon is on screen yet, so an Add
    // or a re-add carries it. The shell is told only while it holds the entry.
    public void Update(string tip, IntPtr icon)
    {
        _tip  = tip.Length > MaxTipLength ? tip[..MaxTipLength] : tip;
        _icon = icon;
        if (!_added) return;

        var data = BuildData(NativeMethods.NIF_ICON | NativeMethods.NIF_TIP);
        api.Notify(NativeMethods.NIM_MODIFY, ref data);
    }

    // After TaskbarCreated: the entry goes in through NIM_ADD again, callback
    // message included, with the last status. A refused add is tried as a
    // modify first: an entry the shell still holds (a broadcast that did not
    // follow a restart) refuses a second add, and the icon is then on screen.
    public TrayIconReaddOutcome Readd()
    {
        _readdAttempts = _readdAttempts >= MaxReaddAttempts ? 1 : _readdAttempts + 1;

        var data = BuildData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP);
        if (api.Notify(NativeMethods.NIM_ADD, ref data) || api.Notify(NativeMethods.NIM_MODIFY, ref data))
        {
            _added = true;
            _readdAttempts = 0;
            return TrayIconReaddOutcome.Added;
        }

        _added = false;
        return _readdAttempts >= MaxReaddAttempts
            ? TrayIconReaddOutcome.GaveUp
            : TrayIconReaddOutcome.RetryLater;
    }

    public void Delete()
    {
        if (!_added) return;
        var data = BuildData(0);
        api.Notify(NativeMethods.NIM_DELETE, ref data);
        _added = false;
    }

    private NOTIFYICONDATA BuildData(uint flags)
    {
        return new NOTIFYICONDATA
        {
            cbSize           = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd             = window,
            uID              = IconId,
            uFlags           = flags,
            uCallbackMessage = NativeMethods.WM_TRAY,
            hIcon            = _icon,
            szTip            = _tip,
            szInfo           = "",
            szInfoTitle      = "",
        };
    }
}

internal sealed class Win32NotifyIconApi : INotifyIconApi
{
    public bool Notify(uint message, ref NOTIFYICONDATA data) =>
        NativeMethods.Shell_NotifyIcon(message, ref data);

    public int LastError => Marshal.GetLastWin32Error();
}
