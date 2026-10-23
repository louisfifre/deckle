using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class DeckleAmbientSource
{
    // ── AmbientPage UI surface ──────────────────────────────────────────

    [Event(EvtAmbientPagePairFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pairing from Settings failed\nex_type={0} | ex_message={1}")]
    public void AmbientPagePairFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientPagePairFailed, ex_type, ex_message);
    }

    [Event(EvtAmbientPageListGroupsFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Listing groups from Settings failed\nex_type={0} | ex_message={1}")]
    public void AmbientPageListGroupsFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientPageListGroupsFailed, ex_type, ex_message);
    }

    // ── AmbientViewModel setters ────────────────────────────────────────

    [Event(EvtTuningChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "setting={0} | value={1}")]
    public void TuningChanged(string setting, string value)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtTuningChanged, setting, value);
    }

    [Event(EvtPipelineModeChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The ambient pipeline mode changed")]
    public void PipelineModeChanged()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtPipelineModeChanged);
    }

    [Event(EvtPipelineModeChangedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "mode={0}")]
    public void PipelineModeChangedDetail(string mode)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtPipelineModeChangedDetail, mode);
    }

    // ── Settings persistence ────────────────────────────────────────────

    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Ambient settings store reported a problem\ndetail={0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Ambient settings store failed\ndetail={0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }

    // The settings file stores the selected tuning preset by id. An id that
    // names no preset (one retired by an update, or a hand-edited file) is
    // replaced by the selection the stored values match, so the tuning on screen
    // can differ from the one the person chose. Both ids are declared preset
    // ids or "custom", never user content.
    [Event(EvtPresetSelectionReplaced,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A stored Ambient preset names no preset, the selection was derived from the values\nasked={0} | obtained={1}")]
    public void PresetSelectionReplaced(string asked, string obtained)
    {
        if (IsEnabled()) WriteEvent(EvtPresetSelectionReplaced, asked, obtained);
    }}
