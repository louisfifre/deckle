using System.Runtime.InteropServices;
using static Deckle.Shell.Taskbar.TaskbarNativeMethods;

namespace Deckle.Shell.Taskbar;

// Calls back each time explorer builds its taskbar: at sign-in, and again after
// every explorer restart. Windows broadcasts the registered message
// "TaskbarCreated" to top-level windows only, so a message-only window would
// never hear it. This listener owns an invisible top-level window and the
// thread that pumps it.
//
// The callback runs on the listener's thread. The taskbar may exist before its
// XAML does, so the caller's injection retries on its own side.
internal sealed class TaskbarCreatedListener : IDisposable
{
    private readonly Action _created;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly string _className = "Deckle.Taskbar.Listener." + Guid.NewGuid().ToString("N");
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Thread _thread;
    private readonly WindowProc _procedure;   // held so the delegate outlives the window
    private nint _hwnd;

    public TaskbarCreatedListener(Action created)
    {
        _created = created;
        _procedure = Handle;
        _thread = new Thread(Run) { IsBackground = true, Name = "Deckle taskbar-created listener" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public bool IsListening => _hwnd != 0;

    private nint Handle(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == _taskbarCreated)
        {
            try { _created(); }
            catch (Exception) { /* a callback must not end the pump */ }
            return 0;
        }
        if (message == WM_DESTROY)
        {
            PostQuitMessage(0);
            return 0;
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void Run()
    {
        nint instance = GetModuleHandle(null);
        nint className = Marshal.StringToHGlobalUni(_className);
        try
        {
            var windowClass = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_procedure),
                hInstance = instance,
                lpszClassName = className,
            };
            if (RegisterClassEx(windowClass) == 0) return;

            // A plain overlapped window that is never shown: top-level, so the
            // broadcast reaches it.
            _hwnd = CreateWindowEx(0, _className, "Deckle taskbar listener", 0, 0, 0, 0, 0, 0, 0, instance, 0);
            _ready.Set();
            if (_hwnd == 0) return;

            while (GetMessage(out Msg message, 0, 0, 0) > 0)
                DispatchMessage(message);
        }
        finally
        {
            _ready.Set();
            _hwnd = 0;
            UnregisterClass(_className, instance);
            Marshal.FreeHGlobal(className);
        }
    }

    public void Dispose()
    {
        nint hwnd = _hwnd;
        if (hwnd != 0) PostMessage(hwnd, WM_CLOSE, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}

