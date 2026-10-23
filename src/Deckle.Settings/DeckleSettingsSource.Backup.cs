using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Settings;

public sealed partial class DeckleSettingsSource
{
    // ── Backup ──────────────────────────────────────────────────────────

    [Event(EvtBackupSkippedSourceMissing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The settings backup was skipped because the settings file is missing")]
    public void BackupSkippedSourceMissing()
    {
        if (IsEnabled()) WriteEvent(EvtBackupSkippedSourceMissing);
    }

    [Event(EvtBackupSkippedSourceMissingDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "path={0}")]
    public void BackupSkippedSourceMissingDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtBackupSkippedSourceMissingDetail, path);
    }

    [Event(EvtBackupCreated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Backup created")]
    public void BackupCreated()
    {
        if (IsEnabled()) WriteEvent(EvtBackupCreated);
    }

    [Event(EvtBackupCreatedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "path={0}")]
    public void BackupCreatedDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtBackupCreatedDetail, path);
    }

    [Event(EvtBackupFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The settings backup could not be created\nex_type={0} | ex_message={1}")]
    public void BackupFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtBackupFailed, ex_type, ex_message);
    }

    [Event(EvtBackupListFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The settings backups could not be listed, so none are offered for restore\nex_type={0} | ex_message={1}")]
    public void BackupListFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtBackupListFailed, ex_type, ex_message);
    }

    [Event(EvtRestoreSkippedSnapshotMissing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The settings restore was skipped because the backup is missing, so the settings did not change")]
    public void RestoreSkippedSnapshotMissing()
    {
        if (IsEnabled()) WriteEvent(EvtRestoreSkippedSnapshotMissing);
    }

    [Event(EvtRestoreSkippedSnapshotMissingDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "path={0}")]
    public void RestoreSkippedSnapshotMissingDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtRestoreSkippedSnapshotMissingDetail, path);
    }

    [Event(EvtRestoredFromBackup,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Restored from backup")]
    public void RestoredFromBackup()
    {
        if (IsEnabled()) WriteEvent(EvtRestoredFromBackup);
    }

    [Event(EvtRestoredFromBackupDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "path={0}")]
    public void RestoredFromBackupDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtRestoredFromBackupDetail, path);
    }

    // reason: snapshot_invalid (the snapshot is not JSON, live file untouched),
    // copy_failed (live file untouched), reload_failed (live file replaced, the
    // in-memory settings may lag until the next launch).
    [Event(EvtRestoreFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Restoring the settings backup failed\nreason={0} | ex_type={1} | ex_message={2}")]
    public void RestoreFailed(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtRestoreFailed, reason, ex_type, ex_message);
    }

    [Event(EvtRestoreDeclined,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The settings restore was declined")]
    public void RestoreDeclined()
    {
        if (IsEnabled()) WriteEvent(EvtRestoreDeclined);
    }

    [Event(EvtCleanupFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "operation={0} | ex_type={1} | ex_message={2}")]
    public void CleanupFailedDetail(string operation, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCleanupFailedDetail, operation, ex_type, ex_message);
    }

}
