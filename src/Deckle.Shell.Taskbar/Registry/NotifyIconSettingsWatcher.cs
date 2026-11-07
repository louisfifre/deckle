using Microsoft.Win32;
using static Deckle.Shell.Taskbar.TaskbarNativeMethods;

namespace Deckle.Shell.Taskbar;

// Calls back when anything under the notify icon settings key changes: a new
// icon's subkey, or a value written on one (brief-csharp-socle B10). It knows
// nothing of IsPromoted; TrayIconPromoter decides what a change means.
//
// One dedicated background thread waits on two events: the registry change,
// armed with RegNotifyChangeKeyValue (subtree, NAME | LAST_SET), and the stop
// event Dispose sets. The registration fires once, so the thread re-arms it
// before each wait; it re-arms before calling back, so a change made during
// the callback (Deckle's own write included) is caught on the next turn rather
// than lost. Changes are debounced: the callback runs once the key has been
// quiet for 200 ms, so an icon that writes its subkey in a burst costs one
// pass. A dedicated thread, not the pool: the registration lives as long as
// the thread that armed it.
//
// The callback runs on that thread. TrayIconPromoter catches what its pass can
// throw; the watcher still guards the call and keeps watching, since an
// unhandled exception on a background thread would end the whole process.
//
// The thread can also end on its own: the key is missing at start, or a
// re-arm fails (the key was deleted under it). IsWatching then reads false,
// and a watcher that had been watching calls lost, on its thread, so its
// owner can start another; one that never armed only reads false, since its
// owner is still waiting on the constructor and sees it there.
internal sealed class NotifyIconSettingsWatcher : IDisposable
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(200);

    private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(2);

    private readonly string _rootPath;
    private readonly Action _changed;
    private readonly Action<NotifyIconSettingsWatcher>? _lost;
    private readonly ManualResetEvent _stop = new(false);
    private readonly ManualResetEventSlim _started = new(false);
    private readonly Thread _thread;
    private volatile bool _watching;
    private bool _disposed;

    /// <param name="rootPath">The HKCU-relative key to watch, subtree included.</param>
    /// <param name="changed">Called on the watcher's thread after each debounced change.</param>
    /// <param name="lost">
    /// Called on the watcher's thread when it stops on its own after it had
    /// been watching; never after <see cref="Dispose"/>.
    /// </param>
    public NotifyIconSettingsWatcher(string rootPath, Action changed, Action<NotifyIconSettingsWatcher>? lost = null)
    {
        _rootPath = rootPath;
        _changed = changed;
        _lost = lost;
        _thread = new Thread(Run) { IsBackground = true, Name = "Deckle tray icon watcher" };
        _thread.Start();
        // Returns once the first notification is armed (or the thread gave
        // up), so a change made right after construction is never missed.
        _started.Wait(_stopTimeout);
    }

    /// <summary>
    /// Whether the thread is armed on the key. False before the first arm,
    /// and for good once the thread ended, by Dispose or on its own.
    /// </summary>
    public bool IsWatching => _watching;

    private void Run()
    {
        DeckleShellTaskbarSource.Log.TrayWatcherStateChanged("started");
        try
        {
            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(_rootPath);
            if (root is null)
            {
                Fail("open", "key-missing");
                return;
            }

            using var change = new AutoResetEvent(false);
            WaitHandle[] handles = [_stop, change];
            bool armed = false;

            while (true)
            {
                if (!Arm(root, change, ref armed)) return;
                _watching = true;
                _started.Set();
                if (WaitHandle.WaitAny(handles) == 0) return;
                armed = false;

                // Wait for the key to go quiet, re-arming after each signal.
                while (true)
                {
                    if (!Arm(root, change, ref armed)) return;
                    int signalled = WaitHandle.WaitAny(handles, Debounce);
                    if (signalled == 0) return;
                    if (signalled == WaitHandle.WaitTimeout) break;
                    armed = false;
                }

                try
                {
                    _changed();
                }
                catch (Exception ex)
                {
                    DeckleShellTaskbarSource.Log.TrayWatcherStateChanged("callback_failed:" + ex.GetType().Name);
                }
            }
        }
        catch (Exception ex)
        {
            // Opening or arming threw rather than returning an error: the
            // thread ends like any other failure instead of the process.
            Fail("watch", ex.GetType().Name);
        }
        finally
        {
            bool wasWatching = _watching;
            _watching = false;
            _started.Set();
            DeckleShellTaskbarSource.Log.TrayWatcherStateChanged("stopped");
            if (wasWatching && !_stop.WaitOne(0)) _lost?.Invoke(this);
        }
    }

    // One registration at a time: arming twice would stack two notifications
    // on the same handle.
    private bool Arm(RegistryKey root, AutoResetEvent change, ref bool armed)
    {
        if (armed) return true;
        int error = RegNotifyChangeKeyValue(
            root.Handle,
            bWatchSubtree: true,
            REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET,
            change.SafeWaitHandle,
            fAsynchronous: true);
        if (error != ERROR_SUCCESS)
        {
            Fail("arm", error.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return false;
        }
        armed = true;
        return true;
    }

    // step "open" (error "key-missing"), "arm" (the Win32 error code) or
    // "watch" (an exception type).
    private static void Fail(string step, string error)
    {
        DeckleShellTaskbarSource.Log.TrayWatcherFailed(step, error);
    }

    /// <summary>
    /// Stops the thread and waits for it, so no callback runs once this
    /// returns (bar one already running past the two-second wait, which only
    /// a hung registry call would cause).
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Set();
        // A thread still running past the wait keeps its stop event: disposing
        // it under the thread would throw there instead of letting it end.
        bool ended = Thread.CurrentThread != _thread && _thread.Join(_stopTimeout);
        if (ended)
        {
            _stop.Dispose();
            _started.Dispose();
        }
    }
}
