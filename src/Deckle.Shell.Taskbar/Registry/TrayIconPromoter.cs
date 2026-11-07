using System.Security;

namespace Deckle.Shell.Taskbar;

// The tier 1 engine of "show all tray icons" (brief-csharp-socle T4): it sets
// IsPromoted=1 on every icon's subkey under HKCU\Control Panel\NotifyIconSettings
// and keeps it there while the setting is on.
//
// Apply snapshots the key into the TraySnapshotStore the first time (every
// subkey, with its IsPromoted or "absent"), writes 1 wherever the value is not
// already 1, and starts the watcher. Each debounced change the watcher reports
// runs the same plan again: a new icon gets promoted, an icon explorer demoted
// gets promoted back, and Deckle's own write finds nothing left to do (no
// loop). Restore stops the watcher, then gives the key back the snapshot and
// forgets it; icons that appeared after enable lose their IsPromoted
// (TrayIconPromotion explains the rule). Dispose stops the watcher and
// restores nothing (brief B8).
//
// Only the first Apply snapshots: a later one (Deckle restarted with the
// setting on, explorer restarted) finds the snapshot there and leaves it, so
// an icon that appeared meanwhile stays out of it and is handed back to
// Windows's default on restore, like any icon that appeared after enable.
// Edge left as is: an enable on a key with no subkey at all records nothing,
// so the next Apply snapshots whatever is there by then, and a restore with
// no snapshot writes nothing (it cannot tell old icons from new ones).
//
// The two writers, the App's settings observer and the watcher's thread, take
// one lock. The watcher is stopped outside it: its callback waits on that
// lock, and joining the thread while holding it would stall the stop.
//
// A watcher can end on its own (its key deleted under it). A lost watcher is
// restarted once, from the pool; Apply replaces one whose thread ended, and
// answers Failed while none watches, so the page says it instead of showing
// success over a dead thread.
//
// LastOutcome and OutcomeChanged tell the page how the last pass ended, from
// any thread. They carry the outcome of the state the setting asks for: Apply's
// while the setting is on, Restore's after it was turned off, and nothing
// before the first pass.
//
// A side effect the page states: while on, hiding an icon from Windows
// Settings does nothing, since explorer's IsPromoted=0 is written back to 1
// (brief A4).
public sealed class TrayIconPromoter : IDisposable
{
    /// <summary>The key explorer keeps its notify icon settings in, under HKCU.</summary>
    public const string DefaultRootPath = @"Control Panel\NotifyIconSettings";

    /// <summary>
    /// Set to 0 to turn the explorer nudge off (NotifyIconSettingsKey explains
    /// it); any other value, or none, leaves it on.
    /// </summary>
    public const string NudgeVariable = "DECKLE_TASKBAR_TRAY_NUDGE";

    private static readonly Lazy<TrayIconPromoter> _instance = new(() => new TrayIconPromoter(
        DefaultRootPath, NudgeFromEnvironment(), new TraySnapshotStore(TraySnapshotStore.DefaultPath)));

    /// <summary>The promoter on the real key, shared by the App and the page.</summary>
    public static TrayIconPromoter Instance => _instance.Value;

    private readonly object _gate = new();
    private readonly NotifyIconSettingsKey _key;
    private readonly TraySnapshotStore _snapshot;
    private NotifyIconSettingsWatcher? _watcher;
    private bool _hasLastOutcome;
    private TrayOutcome _lastOutcome;
    private bool _active;
    private bool _failing;
    private bool _disposed;

    /// <param name="rootPath">
    /// The HKCU-relative key whose subkeys are the icons:
    /// <see cref="DefaultRootPath"/> in the app.
    /// </param>
    /// <param name="nudgeExplorer">
    /// Write then delete a temporary value after each IsPromoted write, so
    /// explorer's per-icon watcher re-reads the icon; see
    /// <see cref="NudgeFromEnvironment"/>.
    /// </param>
    /// <param name="snapshot">Where the prior values are kept between runs.</param>
    internal TrayIconPromoter(string rootPath, bool nudgeExplorer, TraySnapshotStore snapshot)
    {
        _key = new NotifyIconSettingsKey(rootPath, nudgeExplorer);
        _snapshot = snapshot;
    }

    /// <summary>Whether the nudge is on, from <see cref="NudgeVariable"/>.</summary>
    public static bool NudgeFromEnvironment() =>
        Environment.GetEnvironmentVariable(NudgeVariable)?.Trim() != "0";

    /// <summary>
    /// How the last Apply, watcher pass or Restore ended; null before any, and
    /// after a Restore that gave everything back.
    /// </summary>
    public TrayOutcome? LastOutcome
    {
        get
        {
            lock (_gate) return _hasLastOutcome ? _lastOutcome : null;
        }
    }

    /// <summary>
    /// Raised when <see cref="LastOutcome"/> changes, on the thread that ended
    /// the pass (the watcher's, the pool's or the caller's), never under the
    /// promoter's lock.
    /// </summary>
    public event Action? OutcomeChanged;

    /// <param name="trigger">Why the pass runs, for the log: setting, startup or explorer_restart.</param>
    public TrayOutcome Apply(string trigger)
    {
        TrayOutcome outcome;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            outcome = ApplyLocked(trigger);
        }
        Publish(outcome);
        return outcome;
    }

    private TrayOutcome ApplyLocked(string trigger)
    {
        try
        {
            // Watch first, then read: a change landing between the read
            // and the first armed notification would otherwise wait for
            // the next one. The watcher's pass blocks on the lock until
            // this one ends, then finds Deckle's writes and does nothing.
            // A watcher whose thread ended (the key was missing, or a
            // restart after a loss could not arm) is replaced here.
            if (_watcher is { IsWatching: false } dead)
            {
                _watcher = null;
                dead.Dispose();
            }
            _watcher ??= StartWatcher();

            IReadOnlyDictionary<string, uint?>? current = _key.ReadPromotions();
            if (current is null)
            {
                PassFailed(trigger, "key-missing");
                return TrayOutcome.KeyMissing;
            }

            string snapshot = "kept";
            if (_snapshot.IsEmpty && current.Count > 0)
            {
                _snapshot.Record(current);
                snapshot = "taken";
            }

            int promoted = Promote(current);
            if (!_active)
            {
                _active = true;
                DeckleShellTaskbarSource.Log.TrayPromotionStarted();
                DeckleShellTaskbarSource.Log.TrayPromotionStartedDetail(trigger, current.Count, promoted, snapshot);
            }
            else if (promoted > 0)
            {
                DeckleShellTaskbarSource.Log.TrayIconsPromotedDetail(trigger, promoted, current.Count);
            }

            // The icons there now are promoted, but a later one would go
            // to the overflow unseen: the outcome says Failed, so the page
            // says it, and the next Apply retries, watcher included.
            if (!_watcher.IsWatching)
            {
                PassFailed(trigger, "watcher-stopped");
                return TrayOutcome.Failed;
            }
            Recovered();
            return TrayOutcome.Applied;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            PassFailed(trigger, ex.GetType().Name);
            return TrayOutcome.PermissionDenied;
        }
        catch (IOException ex)
        {
            PassFailed(trigger, ex.GetType().Name);
            return TrayOutcome.Failed;
        }
    }

    /// <summary>
    /// Stops watching and gives the icons back what the snapshot recorded. A
    /// no-op while nothing was promoted, so it is safe to call on every start.
    /// </summary>
    public TrayOutcome Restore()
    {
        StopWatching();
        TrayOutcome outcome;
        lock (_gate)
        {
            outcome = RestoreLocked();
        }
        Publish(outcome == TrayOutcome.Applied ? null : outcome);
        return outcome;
    }

    private TrayOutcome RestoreLocked()
    {
        try
        {
            // No key, no icon: nothing to give back.
            IReadOnlyDictionary<string, uint?>? current = _key.ReadPromotions();
            if (current is null)
            {
                Finish(written: 0, icons: 0);
                return TrayOutcome.Applied;
            }

            // No snapshot means no Apply ever got as far as writing (it
            // failed first, or found no icon): every subkey would read as
            // "appeared after enable" and lose its value, so write nothing.
            if (_snapshot.IsEmpty)
            {
                Finish(written: 0, icons: current.Count);
                return TrayOutcome.Applied;
            }

            int written = 0;
            foreach (TrayIconRestoreStep step in TrayIconPromotion.PlanRestore(_snapshot.Entries(), current))
            {
                if (_key.Write(step.Subkey, step.Value)) written++;
            }
            _snapshot.Clear();
            Finish(written, current.Count);
            return TrayOutcome.Applied;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            PassFailed("restore", ex.GetType().Name);
            return TrayOutcome.PermissionDenied;
        }
        catch (IOException ex)
        {
            PassFailed("restore", ex.GetType().Name);
            return TrayOutcome.Failed;
        }
    }

    // The tail of a restore that went through: the end milestone when the
    // promoter had been holding the icons, and the recovery when the last
    // pass had failed.
    private void Finish(int written, int icons)
    {
        if (_active)
        {
            _active = false;
            DeckleShellTaskbarSource.Log.TrayPromotionStopped();
            DeckleShellTaskbarSource.Log.TrayPromotionStoppedDetail(written, icons);
        }
        Recovered();
    }

    // On the watcher's thread, after a debounced change. A pass that writes
    // nothing logs nothing, so an idle key (Deckle's own write included)
    // leaves no trace.
    private void OnKeyChanged()
    {
        TrayOutcome outcome;
        lock (_gate)
        {
            if (_watcher is null || _disposed) return;
            try
            {
                IReadOnlyDictionary<string, uint?>? current = _key.ReadPromotions();
                if (current is null) return;
                int promoted = Promote(current);
                if (promoted > 0) DeckleShellTaskbarSource.Log.TrayIconsPromotedDetail("watcher", promoted, current.Count);
                Recovered();
                outcome = TrayOutcome.Applied;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
            {
                PassFailed("watcher", ex.GetType().Name);
                outcome = TrayOutcome.PermissionDenied;
            }
            catch (Exception ex)
            {
                // The watcher's thread is a background thread: an exception
                // out of here would end the process, so any other failure is
                // a failed pass too.
                PassFailed("watcher", ex.GetType().Name);
                outcome = TrayOutcome.Failed;
            }
        }
        Publish(outcome);
    }

    private NotifyIconSettingsWatcher StartWatcher() => new(_key.RootPath, OnKeyChanged, OnWatcherLost);

    // On the lost watcher's own thread, which is ending: the restart goes to
    // the pool, where it can wait for the lock and join that thread.
    private void OnWatcherLost(NotifyIconSettingsWatcher lost) =>
        ThreadPool.QueueUserWorkItem(_ => RestartWatcher(lost));

    // One restart per loss. The new watcher calls lost only once it had been
    // watching, so a key still missing ends the chain here rather than
    // looping; the next Apply replaces that dead watcher. A pass right after
    // catches the icons that appeared while nothing watched.
    private void RestartWatcher(NotifyIconSettingsWatcher lost)
    {
        lock (_gate)
        {
            // Restore or Dispose stopped it meanwhile, or an Apply replaced it.
            if (_disposed || _watcher != lost) return;
            DeckleShellTaskbarSource.Log.TrayWatcherStateChanged("restarting");
            lost.Dispose();
            _watcher = StartWatcher();
        }
        OnKeyChanged();
    }

    private int Promote(IReadOnlyDictionary<string, uint?> current)
    {
        int promoted = 0;
        foreach (string subkey in TrayIconPromotion.SubkeysToPromote(current))
        {
            if (_key.Write(subkey, TrayIconPromotion.Promoted)) promoted++;
        }
        return promoted;
    }

    // One incident per run of failed passes: one Warning with its cause when
    // the run starts, the recovery when a pass goes through again.
    private void PassFailed(string trigger, string error)
    {
        if (_failing) return;
        _failing = true;
        DeckleShellTaskbarSource.Log.TrayPassFailed(trigger, error);
    }

    private void Recovered()
    {
        if (!_failing) return;
        _failing = false;
        DeckleShellTaskbarSource.Log.TrayPassRecovered();
    }

    // null clears the outcome (a restore that gave everything back). The event
    // is raised only on a change, outside the lock.
    private void Publish(TrayOutcome? outcome)
    {
        lock (_gate)
        {
            bool had = _hasLastOutcome;
            TrayOutcome previous = _lastOutcome;
            if (outcome is null)
            {
                if (!had) return;
                _hasLastOutcome = false;
            }
            else
            {
                if (had && previous == outcome) return;
                _lastOutcome = outcome.Value;
                _hasLastOutcome = true;
            }
        }
        OutcomeChanged?.Invoke();
    }

    private void StopWatching()
    {
        NotifyIconSettingsWatcher? watcher;
        lock (_gate)
        {
            watcher = _watcher;
            _watcher = null;
        }
        watcher?.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        StopWatching();
    }
}
