using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Input.PrecisionScroll;

[EventSource(Name = "Deckle-PrecisionScroll")]
public sealed class DecklePrecisionScrollSource : DeckleEventSource
{
    public static readonly DecklePrecisionScrollSource Log = new();

    private DecklePrecisionScrollSource() { }

    public const int EvtEngineStarted = 1;
    public const int EvtEngineStopped = 2;
    public const int EvtEngineStoppedDetail = 3;
    public const int EvtUnavailable = 4;
    public const int EvtUnavailableDetail = 5;
    public const int EvtTouchpadSettingsUnavailable = 6;
    public const int EvtTouchpadSettingsUnavailableDetail = 7;
    public const int EvtInjectionFailed = 8;
    public const int EvtInjectionFailedDetail = 9;
    public const int EvtQueueOverloaded = 10;

    [Event(EvtEngineStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Precision scrolling enabled")]
    public void EngineStarted()
    {
        if (IsEnabled()) WriteEvent(EvtEngineStarted);
    }

    [Event(EvtEngineStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Precision scrolling disabled")]
    public void EngineStopped()
    {
        if (IsEnabled()) WriteEvent(EvtEngineStopped);
    }

    [Event(EvtEngineStoppedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "precision scrolling closed | detents={0} | gestures={1} | rollovers={2}")]
    public void EngineStoppedDetail(long detents, long gestures, long rollovers)
    {
        if (IsEnabled()) WriteEvent(EvtEngineStoppedDetail, detents, gestures, rollovers);
    }

    [Event(EvtUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Precision scrolling is unavailable")]
    public void Unavailable()
    {
        if (IsEnabled()) WriteEvent(EvtUnavailable);
    }

    [Event(EvtUnavailableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "precision scrolling unavailable | reason={0} | win32_error={1}")]
    public void UnavailableDetail(string reason, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtUnavailableDetail, reason, win32_error);
    }

    [Event(EvtTouchpadSettingsUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Touchpad direction could not be read")]
    public void TouchpadSettingsUnavailable()
    {
        if (IsEnabled()) WriteEvent(EvtTouchpadSettingsUnavailable);
    }

    [Event(EvtTouchpadSettingsUnavailableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "touchpad direction unavailable | win32_error={0}")]
    public void TouchpadSettingsUnavailableDetail(int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtTouchpadSettingsUnavailableDetail, win32_error);
    }

    [Event(EvtInjectionFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Touchpad injection failed")]
    public void InjectionFailed()
    {
        if (IsEnabled()) WriteEvent(EvtInjectionFailed);
    }

    [Event(EvtInjectionFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "touchpad injection failed | action={0} | win32_error={1}")]
    public void InjectionFailedDetail(string action, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtInjectionFailedDetail, action, win32_error);
    }

    [Event(EvtQueueOverloaded,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Precision scrolling stopped to protect input latency")]
    public void QueueOverloaded()
    {
        if (IsEnabled()) WriteEvent(EvtQueueOverloaded);
    }
}
