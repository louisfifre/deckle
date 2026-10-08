using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class DeckleAmbientSource
{
    // ── AmbientPage UI surface ──────────────────────────────────────────

    [Event(EvtAmbientPagePairFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pairing from Settings failed")]
    public void AmbientPagePairFailed()
    {
        if (IsEnabled()) WriteEvent(EvtAmbientPagePairFailed);
    }

    [Event(EvtAmbientPagePairFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pair from settings failed | ex_type={0} | ex_message={1}")]
    public void AmbientPagePairFailedDetail(string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtAmbientPagePairFailedDetail, ex_type, ex_message);
    }

    [Event(EvtAmbientPageListGroupsFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Listing groups from Settings failed")]
    public void AmbientPageListGroupsFailed()
    {
        if (IsEnabled()) WriteEvent(EvtAmbientPageListGroupsFailed);
    }

    [Event(EvtAmbientPageListGroupsFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "list groups from settings failed | ex_type={0} | ex_message={1}")]
    public void AmbientPageListGroupsFailedDetail(string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtAmbientPageListGroupsFailedDetail, ex_type, ex_message);
    }

    // ── AmbientViewModel setters ────────────────────────────────────────

    [Event(EvtTuningChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "tuning changed | setting={0} | value={1}")]
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
           Message = "pipeline mode | mode={0}")]
    public void PipelineModeChangedDetail(string mode)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtPipelineModeChangedDetail, mode);
    }

    // ── Settings persistence ────────────────────────────────────────────

    [Event(EvtSettingsLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoaded(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoaded, message);
    }

    [Event(EvtSettingsLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadComplete(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadComplete, message);
    }

    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }

    [Event(EvtAmbientSettingsPrefixed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void AmbientSettingsPrefixed(string message)
    {
        if (IsEnabled()) WriteEvent(EvtAmbientSettingsPrefixed, message);
    }
}
