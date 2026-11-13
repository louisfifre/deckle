using System.Diagnostics;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Settings;

// Settings module provider: the SettingsWindow shell and General page, the
// backup service and the shell settings persistence (SettingsService).
//
// An Info milestone is a short Capital sentence; ids and paths live in a
// Verbose mirror that follows it. A Warning or Error carries its own cause in
// its payload (ex_type and ex_message for an exception, a snake_case reason for
// a decision), so a trace filtered to Warning still says why. Event ids are
// public in the ETW manifest and are never reused.
[EventSource(Name = "Deckle-Settings")]
public sealed partial class DeckleSettingsSource : DeckleEventSource
{
    public static readonly DeckleSettingsSource Log = new();

    // Restarted once per navigation in OnNavSelectionChanged; NavCompletedDetail
    // reads it.
    // Static so a destination page, which has no window reference, can time its
    // first Loaded against it (PageReady). Single-window UI-thread nav, so no
    // contention.
    public static readonly Stopwatch NavClock = new();

    private DeckleSettingsSource() { }

    // ── Retired: the legacy → per-module migration ──
    // 1 to 15 retired; the migration they reported (SettingsBootstrap) was
    // removed. Never reuse.

    // ── Backup ──
    public const int EvtBackupSkippedSourceMissing        = 16;
    public const int EvtBackupCreated                     = 17;
    // 18 retired.
    // 19 retired.
    public const int EvtRestoreSkippedSnapshotMissing     = 20;
    public const int EvtRestoredFromBackup                = 21;
    // 22 retired.

    // ── Folder picker ──
    // 23 is burned: the folder-picker events live in DeckleSettingsUxSource.
    // Never reuse.

    // ── General page (setup wizard) ──
    public const int EvtSetupWizardHookNotWired           = 24;
    public const int EvtSetupWindowOpenedFromSettings     = 25;
    // 26 retired.
    // 27 retired.

    // ── SettingsWindow navigation ──
    public const int EvtNavSelectionChanged               = 28;
    public const int EvtNavSelectionIgnored               = 29;
    public const int EvtNavImpossibleNoTag                = 30;
    public const int EvtNavFailedTypeNotFound             = 31;
    public const int EvtNavSkippedAlreadyCurrent          = 32;
    public const int EvtNavStarted                        = 33;
    public const int EvtNavFailedFrameRejected            = 34;
    public const int EvtNavCompleted                      = 35;
    // 36 retired.
    public const int EvtNavStackTrace                     = 37;
    // 38 retired.
    public const int EvtOpenLogsFromFooter                = 39;

    // ── ViewModels (generic) ──
    // 40, 41 and 42 are burned: SettingChanged and SectionReset live in the
    // shared DeckleSettingsUxSource, so a module page emits them without a
    // reference to the shell. Never reuse.

    // ── Settings persistence (transitional) ──
    // Free-text "{0}" events: JsonSettingsStore lives in Deckle.Core, which has
    // no EventSource, and hands its lines over as strings. Each line already
    // follows "Short sentence | reason=x" or "lowercase words | field=value".
    public const int EvtSettingsLoaded                    = 43;
    public const int EvtSettingsLoadComplete              = 44;
    public const int EvtSettingsLoadWarning               = 45;
    public const int EvtSettingsLoadError                 = 46;

    // ── Verbose mirrors ──
    // 47 retired (migration detail).
    // 48 retired.
    // 49 retired.
    // 50 retired (migration detail).
    // 51 retired.
    // 52 retired (migration detail).
    public const int EvtBackupSkippedSourceMissingDetail  = 53;
    public const int EvtBackupCreatedDetail               = 54;
    // 55 retired.
    // 56 retired.
    public const int EvtRestoreSkippedSnapshotMissingDetail = 57;
    public const int EvtRestoredFromBackupDetail          = 58;
    // 59 retired.
    // 60 is burned (folder picker, DeckleSettingsUxSource). Never reuse.
    // 61 retired.
    // 62 retired.
    public const int EvtNavImpossibleNoTagDetail          = 63;
    public const int EvtNavFailedTypeNotFoundDetail       = 64;
    public const int EvtNavStartedDetail                  = 65;
    public const int EvtNavFailedFrameRejectedDetail      = 66;
    public const int EvtNavCompletedDetail                = 67;
    // 68 retired.
    // 69 is burned (section reset, DeckleSettingsUxSource). Never reuse.

    // ── Page navigation timing (ms) ──
    // Numbers ⇒ Verbose only.
    // 70 retired: NavTiming, folded into NavCompletedDetail's duration_ms.
    public const int EvtPageReady                         = 71;

    // ── Settings module nav registry ──
    // Plumbing detail ⇒ Verbose; a tag that fails to resolve surfaces through
    // the NavFailedTypeNotFound milestone when the item is selected.
    public const int EvtSettingsModuleRegistered          = 72;
    public const int EvtSettingsModuleUnregistered        = 73;

    // ── Settings cross-page search index ──
    // A search entry whose header key does not resolve when the index is built,
    // skipped so the rest of the page still indexes. Verbose, no user milestone.
    public const int EvtSearchEntrySkipped                = 74;

    // ── Settings cross-page search (TitleBar box) ───────────────────────
    // SearchExecuted logs the query length, never the text (privacy), and fires
    // per settled keystroke ⇒ Verbose. Picking a hit is a deliberate user action
    // ⇒ Info milestone, its page/card tags in the Verbose mirror.
    public const int EvtSearchExecuted                    = 75;
    public const int EvtSearchNavigated                   = 76;
    public const int EvtSearchNavigatedDetail             = 77;

    // ── Settings TitleBar layout & search presentation ──
    // The observables that let a layout defect at an unseen window size be read
    // back from a trace. Widths in DIPs ⇒ Verbose throughout.
    public const int EvtTitleBarLayout                    = 78;
    public const int EvtSearchPresentationChanged         = 79;
    public const int EvtSearchFocusReleased               = 80;

    // ── Settings module nav nesting ──
    // A child whose ParentId names no registered module gets no rail item; the
    // rest of the band still builds. Verbose.
    public const int EvtSettingsModuleParentMissing       = 81;

    // ── Failure milestones carrying their cause ──
    // They replace the cause-less milestone + Verbose mirror pairs retired above.
    // 82, 83 and 84 retired (migration failures).
    public const int EvtBackupFailed                      = 85;
    public const int EvtBackupListFailed                  = 86;
    public const int EvtRestoreFailed                     = 87;
    public const int EvtSetupWindowOpenFailed             = 88;
    public const int EvtNavFailedThrew                    = 89;

    // ── User declines and best-effort measures ──
    public const int EvtRestoreDeclined                   = 90;
    public const int EvtDataSizeFilesSkippedDetail        = 91;
    public const int EvtCleanupFailedDetail               = 92;

    // ── SettingsWindow title bar commands ──
    // Sibling of EvtOpenLogsFromFooter (39), declared later so it got the next id.
    public const int EvtOpenPlaygroundFromFooter          = 93;
    public const int EvtPageOpened                        = 94;

}
