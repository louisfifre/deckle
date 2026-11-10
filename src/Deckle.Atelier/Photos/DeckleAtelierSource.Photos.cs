using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Atelier;

// The photo tool's events, in their own half of the Atelier's provider so each
// tool keeps its events beside its code. EventIds 100-199 belong to Photos.
//
// Privacy (spec, Behaviours): no photo and no photo metadata leaves the
// machine, and the log is a record that travels. An event here carries
// counts, extensions, reasons and error types, never a name, a path or a value
// read inside a photo.
public sealed partial class DeckleAtelierSource
{
    public const int EvtCaptureDataReadFailed = 100;
    public const int EvtPhotoMarksLoadFailed  = 101;
    public const int EvtPhotoMarksSaveFailed  = 102;
    public const int EvtMarksApplyStarted     = 103;
    public const int EvtMarksApplyEnded       = 104;
    public const int EvtPhotoRecycleRefused   = 105;
    public const int EvtPhotoDeletedForGood   = 106;
    public const int EvtPhotoRenameFailed     = 107;
    public const int EvtDarktableOpened       = 108;
    public const int EvtDarktableNotOpened    = 109;

    // One photo whose capture data could not be read: the panel shows the
    // photo without its "Prise de vue" rows. Verbose, because a folder of a
    // format the parser does not know would otherwise write one warning per
    // file.
    [Event(EvtCaptureDataReadFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "extension={0} | failure={1} | ex_type={2}")]
    public void CaptureDataReadFailed(string extension, string failure, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureDataReadFailed, extension, failure, ex_type);
    }

    [Event(EvtPhotoMarksLoadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The marks of a folder could not be read; its photos show without a mark\nex_type={0}")]
    public void PhotoMarksLoadFailed(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoMarksLoadFailed, ex_type);
    }

    [Event(EvtPhotoMarksSaveFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The marks of a folder could not be saved; they hold until the window closes\nex_type={0}")]
    public void PhotoMarksSaveFailed(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoMarksSaveFailed, ex_type);
    }

    // Start and end of one application of the marks, paired by run_id.
    [Event(EvtMarksApplyStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "Applying the marks started")]
    public void MarksApplyStarted(string run_id, int recycles, int renames)
    {
        if (IsEnabled()) WriteEvent(EvtMarksApplyStarted, run_id, recycles, renames);
    }

    [Event(EvtMarksApplyEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "Applying the marks ended")]
    public void MarksApplyEnded(string run_id, int recycled, int renamed, int failed, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtMarksApplyEnded, run_id, recycled, renamed, failed, elapsed_ms);
    }

    // The shell said it would not recycle the photo, or failed: the photo is
    // left in its folder, never deleted for good, and the apply goes on.
    [Event(EvtPhotoRecycleRefused,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A photo stayed in its folder because the Recycle Bin could not take it\nreason={0} | hresult={1}")]
    public void PhotoRecycleRefused(string reason, int hresult)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoRecycleRefused, reason, hresult);
    }

    // The guard did not hold: the shell reported a deletion with no item in
    // the Recycle Bin. Error, because the person was promised the bin.
    [Event(EvtPhotoDeletedForGood,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A photo was deleted for good instead of going to the Recycle Bin\nextension={0}")]
    public void PhotoDeletedForGood(string extension)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDeletedForGood, extension);
    }

    [Event(EvtPhotoRenameFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A photo could not be renamed; it keeps its name\nreason={0} | ex_type={1}")]
    public void PhotoRenameFailed(string reason, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoRenameFailed, reason, ex_type);
    }

    [Event(EvtDarktableOpened,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "darktable was opened on a folder")]
    public void DarktableOpened(string found_by)
    {
        if (IsEnabled()) WriteEvent(EvtDarktableOpened, found_by);
    }

    [Event(EvtDarktableNotOpened,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "darktable was not opened")]
    public void DarktableNotOpened(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtDarktableNotOpened, reason);
    }
}
