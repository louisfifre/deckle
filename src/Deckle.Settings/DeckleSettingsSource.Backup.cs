using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Settings;

public sealed partial class DeckleSettingsSource
{
    // ── Backup ──────────────────────────────────────────────────────────

    [Event(EvtBackupSkippedSourceMissing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Backup skipped because the source file is missing")]
    public void BackupSkippedSourceMissing()
    {
        if (IsEnabled()) WriteEvent(EvtBackupSkippedSourceMissing);
    }

    [Event(EvtBackupSkippedSourceMissingDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "backup skipped — source missing | path={0}")]
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
           Message = "backup created | path={0}")]
    public void BackupCreatedDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtBackupCreatedDetail, path);
    }

    [Event(EvtBackupFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Backup failed")]
    public void BackupFailed()
    {
        if (IsEnabled()) WriteEvent(EvtBackupFailed);
    }

    [Event(EvtBackupFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "backup failed | error={0} | message={1}")]
    public void BackupFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtBackupFailedDetail, ex_type, message);
    }

    [Event(EvtBackupListFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not list the backups")]
    public void BackupListFailed()
    {
        if (IsEnabled()) WriteEvent(EvtBackupListFailed);
    }

    [Event(EvtBackupListFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "backup list failed | error={0} | message={1}")]
    public void BackupListFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtBackupListFailedDetail, ex_type, message);
    }

    [Event(EvtRestoreSkippedSnapshotMissing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Restore skipped because the snapshot is missing")]
    public void RestoreSkippedSnapshotMissing()
    {
        if (IsEnabled()) WriteEvent(EvtRestoreSkippedSnapshotMissing);
    }

    [Event(EvtRestoreSkippedSnapshotMissingDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "restore skipped — snapshot missing | path={0}")]
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
           Message = "restored from backup | path={0}")]
    public void RestoredFromBackupDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtRestoredFromBackupDetail, path);
    }

    [Event(EvtRestoreFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Restore failed")]
    public void RestoreFailed()
    {
        if (IsEnabled()) WriteEvent(EvtRestoreFailed);
    }

    [Event(EvtRestoreFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "restore failed | error={0} | message={1}")]
    public void RestoreFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtRestoreFailedDetail, ex_type, message);
    }

}
