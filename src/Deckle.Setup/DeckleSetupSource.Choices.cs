using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

public sealed partial class DeckleSetupSource
{
    // ── Choices page ──────────────────────────────────────────────────────────

    [Event(EvtNativeSourcePicked,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A native runtime source was picked")]
    public void NativeSourcePicked()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeSourcePicked);
    }

    [Event(EvtNativeSourcePickedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "native source | source={0} | copied={1}")]
    public void NativeSourcePickedDetail(string source, int copied)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeSourcePickedDetail, source, copied);
    }

    [Event(EvtNativeImportFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Importing the native runtime failed")]
    public void NativeImportFailed()
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeImportFailed);
    }

    [Event(EvtNativeImportFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "native import failed | error={0}")]
    public void NativeImportFailedDetail(string error)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeImportFailedDetail, error);
    }

    [Event(EvtChoicesConfirmed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Setup choices were confirmed")]
    public void ChoicesConfirmed()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtChoicesConfirmed);
    }

    [Event(EvtChoicesConfirmedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "choices confirmed | location={0} | model={1}")]
    public void ChoicesConfirmedDetail(string location, string model)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtChoicesConfirmedDetail, location, model);
    }

}
