using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

public sealed partial class DeckleSetupSource
{
    // ── Native runtime download (InstallingPage) ──────────────────────────────

    [Event(EvtNativeInstalled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The native runtime was installed")]
    public void NativeInstalled()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeInstalled);
    }

    [Event(EvtNativeInstalledDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "native ok | bundle={0} | bytes={1} | dur_ms={2} | sha256={3}")]
    public void NativeInstalledDetail(string bundle, long bytes, long dur_ms, string sha256)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeInstalledDetail, bundle, bytes, dur_ms, sha256);
    }

    [Event(EvtNativeDownloadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The native runtime download failed")]
    public void NativeDownloadFailed()
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeDownloadFailed);
    }

    [Event(EvtNativeDownloadFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "native download failed | error={0}")]
    public void NativeDownloadFailedDetail(string error)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeDownloadFailedDetail, error);
    }

    [Event(EvtNativeRuntimeAborted,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The native runtime download was aborted")]
    public void NativeRuntimeAborted()
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeRuntimeAborted);
    }

    [Event(EvtNativeRuntimeAbortedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "native runtime aborted | reason={0}")]
    public void NativeRuntimeAbortedDetail(string reason)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeRuntimeAbortedDetail, reason);
    }

    [Event(EvtNativeBundleIncomplete,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The native runtime bundle is incomplete")]
    public void NativeBundleIncomplete()
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeBundleIncomplete);
    }

    [Event(EvtNativeBundleIncompleteDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "native incomplete | extracted={0} | expected={1}")]
    public void NativeBundleIncompleteDetail(int extracted, int expected)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeBundleIncompleteDetail, extracted, expected);
    }

    [Event(EvtNativeCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The native runtime download was cancelled")]
    public void NativeCancelled()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtNativeCancelled);
    }

    // ── Model item download (InstallingPage) ──────────────────────────────────

    [Event(EvtItemInstalled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A setup item was installed")]
    public void ItemInstalled()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemInstalled);
    }

    [Event(EvtItemInstalledDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item ok | id={0} | bytes={1} | dur_ms={2} | sha256={3}")]
    public void ItemInstalledDetail(string id, long bytes, long dur_ms, string sha256)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemInstalledDetail, id, bytes, dur_ms, sha256);
    }

    [Event(EvtItemDownloadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A setup item failed to download")]
    public void ItemDownloadFailed()
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemDownloadFailed);
    }

    [Event(EvtItemDownloadFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item failed | id={0} | error={1}")]
    public void ItemDownloadFailedDetail(string id, string error)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemDownloadFailedDetail, id, error);
    }

    [Event(EvtItemCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A setup item download was cancelled")]
    public void ItemCancelled()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemCancelled);
    }

    [Event(EvtItemCancelledDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item cancelled | id={0}")]
    public void ItemCancelledDetail(string id)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemCancelledDetail, id);
    }

}
