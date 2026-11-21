using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Folders;

// The photo descriptions' events, in the Photos range: EventIds 110-129 are
// this block's, so the photo tool's other work keeps 100-109 and the blocks
// after it.
//
// Privacy (spec, Behaviours; task rule): a line carries counts, durations,
// outcomes and failure kinds. Never a description's text, a photo's name or
// path, its capture data or its place, nor what the model answered.
public sealed partial class DeckleFoldersSource
{
    public const int EvtPhotoDescriptionRunStarted      = 110;
    public const int EvtPhotoDescriptionRunEnded        = 111;
    public const int EvtPhotoDescriptionStarted         = 112;
    public const int EvtPhotoDescriptionEnded           = 113;
    public const int EvtPhotoDescribedDetail            = 114;
    public const int EvtPhotoDescriptionFailed          = 115;
    public const int EvtPhotoDescriptionRunAbandoned    = 116;
    public const int EvtPhotoDescriptionRunPartial      = 117;
    public const int EvtPhotoDescriberFaulted           = 118;
    public const int EvtPhotoDescriptionsLoadFailed     = 119;
    public const int EvtPhotoDescriptionsSaveFailed     = 120;
    public const int EvtPhotoDescriptionModelChoiceUnreadable = 121;
    public const int EvtPhotoDescriptionModelChoiceSaveFailed = 122;
    public const int EvtPhotoPictureSourceFailed        = 123;
    public const int EvtPhotoDescriptionNotStarted      = 124;
    public const int EvtPhotoDescriptionRunsOutliveWindow = 125;

    // A folder run, paired by run_id: started with the folder's photos and
    // how many had a description already; ended with its outcome
    // ("Completed", "Stopped", "Failed") and its counts.
    [Event(EvtPhotoDescriptionRunStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "Describing a folder's photos started")]
    public void PhotoDescriptionRunStarted(long run_id, long photos, long already_described)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescriptionRunStarted, run_id, photos, already_described);
    }

    [Event(EvtPhotoDescriptionRunEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "Describing a folder's photos ended")]
    public void PhotoDescriptionRunEnded(long run_id, string outcome, int described, int photos, int failed, int skipped, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescriptionRunEnded, run_id, outcome, described, photos, failed, skipped, elapsed_ms);
    }

    // One photo asked alone, paired by request_id.
    [Event(EvtPhotoDescriptionStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "Describing a photo started")]
    public void PhotoDescriptionStarted(long request_id)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescriptionStarted, request_id);
    }

    [Event(EvtPhotoDescriptionEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "Describing a photo ended")]
    public void PhotoDescriptionEnded(long request_id, string outcome, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescriptionEnded, request_id, outcome, elapsed_ms);
    }

    // One photo of a run: detail, because a run of four hundred would
    // otherwise write four hundred lines; the run's end carries the counts.
    [Event(EvtPhotoDescribedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "run_id={0} | outcome={1} | elapsed_ms={2}")]
    public void PhotoDescribedDetail(long run_id, string outcome, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescribedDetail, run_id, outcome, elapsed_ms);
    }

    // A photo asked alone got no description; the panel says why.
    [Event(EvtPhotoDescriptionFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A photo could not be described; the panel says why\nrequest_id={0} | failure={1}")]
    public void PhotoDescriptionFailed(long request_id, string failure)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescriptionFailed, request_id, failure);
    }

    // A failure that holds for every photo ended the run.
    [Event(EvtPhotoDescriptionRunAbandoned,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Describing a folder stopped: the model cannot be used\nrun_id={0} | failure={1} | failed={2}")]
    public void PhotoDescriptionRunAbandoned(long run_id, string failure, int failed)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescriptionRunAbandoned, run_id, failure, failed);
    }

    // Some photos of a run got no description, the others did: one line per
    // run, not one per photo.
    [Event(EvtPhotoDescriptionRunPartial,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Some photos of a folder could not be described; the run went on without them\nrun_id={0} | failed={1}")]
    public void PhotoDescriptionRunPartial(long run_id, long failed)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescriptionRunPartial, run_id, failed);
    }

    [Event(EvtPhotoDescriberFaulted,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The photo describer threw; the photo counts as failed and the queue goes on\nex_type={0}")]
    public void PhotoDescriberFaulted(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescriberFaulted, ex_type);
    }

    [Event(EvtPhotoDescriptionsLoadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The descriptions of a folder could not be read; its photos show without one\nex_type={0}")]
    public void PhotoDescriptionsLoadFailed(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescriptionsLoadFailed, ex_type);
    }

    [Event(EvtPhotoDescriptionsSaveFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The descriptions of a folder could not be saved; they hold until the window closes\nex_type={0}")]
    public void PhotoDescriptionsSaveFailed(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescriptionsSaveFailed, ex_type);
    }

    [Event(EvtPhotoDescriptionModelChoiceUnreadable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The model chosen for descriptions could not be read; no model is chosen\nex_type={0}")]
    public void PhotoDescriptionModelChoiceUnreadable(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescriptionModelChoiceUnreadable, ex_type);
    }

    [Event(EvtPhotoDescriptionModelChoiceSaveFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The model chosen for descriptions could not be saved; the previous choice stays\nex_type={0}")]
    public void PhotoDescriptionModelChoiceSaveFailed(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescriptionModelChoiceSaveFailed, ex_type);
    }

    // One way of making the picture failed and the next is tried: "preview"
    // (a raw's embedded JPEG), "codec" (the file through its codec), "shell"
    // (the shell's thumbnail). Detail: a format with no codec would
    // otherwise write a line per photo.
    [Event(EvtPhotoPictureSourceFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "source={0} | extension={1} | ex_type={2}")]
    public void PhotoPictureSourceFailed(string source, string extension, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoPictureSourceFailed, source, extension, ex_type);
    }

    // The person asked for a description ("photo" alone or a "folder") and
    // none started, because the model cannot be used as set up: no model
    // chosen, or a provider that is not on this machine. The panel or the
    // command says which and leads to the setting.
    [Event(EvtPhotoDescriptionNotStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "A description was not started: the model is not set up")]
    public void PhotoDescriptionNotStarted(string scope, string failure)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescriptionNotStarted, scope, failure);
    }

    // Deckle Folders window closed while folders were being described: the runs
    // go on (they belong to the app, not to the window), and the folder shows
    // their progress again when it is opened.
    [Event(EvtPhotoDescriptionRunsOutliveWindow,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "Deckle Folders window closed; the folders being described go on")]
    public void PhotoDescriptionRunsOutliveWindow(int runs)
    {
        if (IsEnabled()) WriteEvent(EvtPhotoDescriptionRunsOutliveWindow, runs);
    }
}
